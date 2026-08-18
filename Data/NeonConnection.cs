using Npgsql;

namespace HopMop.Data
{
    /// <summary>
    /// The outcome of resolving the database configuration: a connection string
    /// Npgsql accepts, plus what had to be adjusted to get there so startup can
    /// say so in the log.
    /// </summary>
    public sealed record NeonConnectionInfo(
        string ConnectionString,
        string Target,
        bool UpgradedTls,
        bool VerifiesServerIdentity);

    // Turns whatever the host happens to supply into a connection string Npgsql
    // accepts, and fills in the settings a Neon-hosted database needs.
    //
    // Two things made this worth extracting from Program.cs. First, the Neon
    // dashboard (and its Vercel integration) hands out a libpq URI —
    // postgresql://user:pass@host/db?sslmode=require&channel_binding=require —
    // which Npgsql cannot parse at all, so pasting one straight into the config
    // produced an app that would not start. Second, that URI carries libpq query
    // parameters that have no Npgsql equivalent, and an unknown keyword is a hard
    // error rather than something Npgsql ignores.
    public static class NeonConnection
    {
        // Applied only where the supplied string says nothing, so an explicit
        // value from the host wins over these.
        //
        // Both timeouts are raised from the Npgsql defaults (15s/30s) because a
        // Neon compute that has auto-suspended has to wake before it can answer,
        // and whoever connects first pays for that.
        private const string Defaults =
            "SSL Mode=VerifyFull;Channel Binding=Require;Timeout=30;Command Timeout=60";

        // libpq spells its SSL modes with hyphens; Npgsql uses enum names.
        private static readonly Dictionary<string, string> SslModes =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["disable"] = "Disable",
                ["allow"] = "Allow",
                ["prefer"] = "Prefer",
                ["require"] = "Require",
                ["verify-ca"] = "VerifyCA",
                ["verify-full"] = "VerifyFull",
            };

        // The libpq query parameters that have a direct Npgsql counterpart.
        // Anything outside this list is dropped rather than forwarded: Neon adds
        // parameters like "options=endpoint%3D..." and "pgbouncer=true" that
        // Npgsql does not know.
        private static readonly Dictionary<string, string> QueryKeywords =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["sslmode"] = "SSL Mode",
                ["ssl_mode"] = "SSL Mode",
                ["channel_binding"] = "Channel Binding",
                ["application_name"] = "Application Name",
                ["connect_timeout"] = "Timeout",
            };

        // Encryption without certificate validation: the traffic cannot be read
        // in transit, but nothing checks that the server on the other end is
        // really Neon.
        private static readonly SslMode[] UnverifiedModes =
            [SslMode.Allow, SslMode.Prefer, SslMode.Require];

        /// <summary>
        /// Reads the database connection string from configuration and returns it
        /// in the form Npgsql expects. Throws when none is configured.
        /// </summary>
        public static NeonConnectionInfo Resolve(IConfiguration configuration)
        {
            // DATABASE_URL is the name Neon's own integrations set, so accepting
            // it means a Neon-provisioned environment works without renaming
            // anything by hand. The app's own key is checked first so an explicit
            // setting still overrides an inherited one.
            var raw = configuration.GetConnectionString("DefaultConnection");
            if (string.IsNullOrWhiteSpace(raw))
            {
                raw = configuration["DATABASE_URL"];
            }

            if (string.IsNullOrWhiteSpace(raw))
            {
                throw new InvalidOperationException(
                    "No database connection string is configured. Set " +
                    "ConnectionStrings__DefaultConnection (or DATABASE_URL) to the " +
                    "connection string from your Neon project. Both the " +
                    "postgresql://... URI and the Host=...;Database=... form are accepted.");
            }

            return Normalize(
                raw,
                configuration.GetValue("Database:AllowUnverifiedTls", false));
        }

        /// <summary>
        /// Accepts either a libpq URI or an Npgsql keyword string and returns a
        /// keyword string with the Neon defaults filled in.
        /// </summary>
        /// <param name="allowUnverifiedTls">
        /// Keeps a weaker supplied SSL mode instead of raising it to VerifyFull.
        /// The escape hatch for a runtime with no usable CA bundle, where
        /// certificate validation cannot succeed at all.
        /// </param>
        public static NeonConnectionInfo Normalize(string raw, bool allowUnverifiedTls = false)
        {
            var keywords = LooksLikeUri(raw) ? UriToKeywords(raw) : raw.Trim();

            // Duplicate keywords resolve to the last one seen, so putting the
            // defaults first is what lets the supplied string override them.
            NpgsqlConnectionStringBuilder builder;
            try
            {
                builder = new NpgsqlConnectionStringBuilder($"{Defaults};{keywords}");
            }
            catch (Exception ex) when (ex is ArgumentException or FormatException)
            {
                // Npgsql throws "Couldn't set <keyword>" with the offending key
                // buried in an inner exception, which on its own reads like a bug
                // in the app. The single likeliest cause here is worth naming: a
                // string left over from the SQLite the project used before Neon
                // starts with "Data Source=", and every keyword in it is unknown
                // to Npgsql.
                var hint = keywords.Contains("Data Source", StringComparison.OrdinalIgnoreCase)
                    ? " It looks like a SQLite connection string — this app has used " +
                      "PostgreSQL since it moved to Neon, so replace it with the " +
                      "connection string from your Neon project."
                    : " Check it against the two accepted forms: postgresql://user:pass@host/db " +
                      "or Host=...;Database=...;Username=...;Password=...";

                throw new InvalidOperationException(
                    $"The database connection string could not be parsed by Npgsql.{hint}", ex);
            }

            // Neon's dashboard hands out sslmode=require, and older guides show
            // "SSL Mode=Require" too — boilerplate rather than a considered
            // choice, and it leaves the connection open to an impostor server.
            // Neon presents a publicly trusted certificate, so VerifyFull needs
            // no extra configuration and is what Neon's own .NET guidance
            // specifies. Raising it here rather than only warning about it is
            // what keeps a copy-pasted string from quietly staying the weak one.
            //
            // "Trust Server Certificate" needs no handling: Npgsql 8 marked it
            // obsolete and ignores it outright, so a string carrying it is
            // already governed by SSL Mode alone.
            var upgradedTls = false;
            if (!allowUnverifiedTls && UnverifiedModes.Contains(builder.SslMode))
            {
                builder.SslMode = SslMode.VerifyFull;
                upgradedTls = true;
            }

            // Neon's pooled endpoint is PgBouncer, which hands back a connection
            // that is already in a known state. Npgsql's usual DISCARD ALL on
            // release is pure overhead there, and one extra round trip per
            // request to a database that is not in the same building is worth
            // skipping.
            //
            // Re-parsed from scratch rather than assigned, because Npgsql's
            // ContainsKey answers "is this a known keyword", not "was it set" —
            // so there is no way to ask the builder whether the host supplied
            // this one. Putting it in front of the already-normalized string
            // keeps an explicit value winning, exactly like the defaults above.
            if (builder.Host?.Contains("-pooler", StringComparison.OrdinalIgnoreCase) == true)
            {
                builder = new NpgsqlConnectionStringBuilder(
                    $"No Reset On Close=true;{builder.ConnectionString}");
            }

            return new NeonConnectionInfo(
                builder.ConnectionString,
                $"{builder.Host}/{builder.Database}",
                upgradedTls,
                builder.SslMode is SslMode.VerifyCA or SslMode.VerifyFull);
        }

        /// <summary>
        /// True when the value is a postgres:// or postgresql:// URI rather than
        /// a semicolon-separated keyword string.
        /// </summary>
        public static bool LooksLikeUri(string value)
        {
            var trimmed = value.TrimStart();
            return trimmed.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase);
        }

        private static string UriToKeywords(string value)
        {
            Uri uri;
            try
            {
                uri = new Uri(value.Trim());
            }
            catch (UriFormatException ex)
            {
                throw new InvalidOperationException(
                    "The database connection string looks like a postgresql:// URI but " +
                    "could not be parsed. Copy it again from the Neon dashboard, or use " +
                    "the Host=...;Database=...;Username=...;Password=... form instead.", ex);
            }

            var builder = new NpgsqlConnectionStringBuilder
            {
                Host = uri.Host,
                Database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')),
            };

            // Uri reports -1 when the URI omits the port, and Neon's URIs do.
            if (uri.Port > 0)
            {
                builder.Port = uri.Port;
            }

            // Both halves are percent-encoded in a URI, and a generated Neon
            // password routinely contains characters that get escaped — so
            // decoding is not optional.
            var credentials = uri.UserInfo.Split(':', 2);
            if (credentials[0].Length > 0)
            {
                builder.Username = Uri.UnescapeDataString(credentials[0]);
            }
            if (credentials.Length == 2)
            {
                builder.Password = Uri.UnescapeDataString(credentials[1]);
            }

            foreach (var (key, raw) in ParseQuery(uri.Query))
            {
                if (!QueryKeywords.TryGetValue(key, out var keyword))
                {
                    continue;
                }

                builder[keyword] = keyword == "SSL Mode" && SslModes.TryGetValue(raw, out var sslMode)
                    ? sslMode
                    : raw;
            }

            return builder.ConnectionString;
        }

        private static IEnumerable<KeyValuePair<string, string>> ParseQuery(string query)
        {
            foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var separator = part.IndexOf('=');
                if (separator <= 0) continue;

                yield return new KeyValuePair<string, string>(
                    Uri.UnescapeDataString(part[..separator]),
                    Uri.UnescapeDataString(part[(separator + 1)..]));
            }
        }
    }
}
