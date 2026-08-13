// Guided photo upload (Admin -> Upload).
// Shows a thumbnail of the picked file, supports drag & drop, and explains
// problems in plain Bulgarian before the form is ever submitted.
// Everything here is optional: without JS the plain file inputs still work.
document.addEventListener('DOMContentLoaded', function () {
  var form = document.querySelector('.upload-form');
  if (!form) return;

  var MAX_BYTES = 10 * 1024 * 1024; // 10 MB
  var ALLOWED = ['image/jpeg', 'image/png', 'image/webp'];

  function humanSize(bytes) {
    return (bytes / (1024 * 1024)).toFixed(1).replace('.', ',') + ' MB';
  }

  function setError(zone, message) {
    var box = zone.querySelector('.dz-error');
    if (!message) {
      if (box) box.remove();
      return;
    }
    if (!box) {
      box = document.createElement('span');
      box.className = 'dz-error';
      zone.appendChild(box);
    }
    box.textContent = message;
  }

  function clearZone(zone, input) {
    input.value = '';
    zone.classList.remove('has-file');
    var img = zone.querySelector('.dz-preview');
    if (img && img.dataset.objectUrl) {
      URL.revokeObjectURL(img.dataset.objectUrl);
      delete img.dataset.objectUrl;
      img.removeAttribute('src');
    }
  }

  function showFile(zone, input, file) {
    if (ALLOWED.indexOf(file.type) === -1) {
      setError(zone, 'Този файл не е снимка. Изберете файл във формат JPG, PNG или WEBP.');
      clearZone(zone, input);
      return;
    }
    if (file.size > MAX_BYTES) {
      setError(zone, 'Снимката е твърде голяма (' + humanSize(file.size) + '). Максимумът е 10 MB.');
      clearZone(zone, input);
      return;
    }

    setError(zone, null);

    var img = zone.querySelector('.dz-preview');
    var name = zone.querySelector('.dz-name');
    if (img) {
      if (img.dataset.objectUrl) URL.revokeObjectURL(img.dataset.objectUrl);
      var url = URL.createObjectURL(file);
      img.dataset.objectUrl = url;
      img.src = url;
    }
    if (name) name.textContent = file.name + ' · ' + humanSize(file.size);
    zone.classList.add('has-file');
  }

  Array.prototype.forEach.call(document.querySelectorAll('.dropzone'), function (zone) {
    var input = zone.querySelector('input[type="file"]');
    if (!input) return;

    input.addEventListener('change', function () {
      if (input.files && input.files[0]) showFile(zone, input, input.files[0]);
      else clearZone(zone, input);
    });

    ['dragenter', 'dragover'].forEach(function (evt) {
      zone.addEventListener(evt, function (e) {
        e.preventDefault();
        zone.classList.add('is-over');
      });
    });

    ['dragleave', 'dragend', 'drop'].forEach(function (evt) {
      zone.addEventListener(evt, function () { zone.classList.remove('is-over'); });
    });

    zone.addEventListener('drop', function (e) {
      e.preventDefault();
      var dropped = e.dataTransfer && e.dataTransfer.files;
      if (!dropped || !dropped.length) return;

      // Hand the dropped file to the real input so it posts with the form.
      try {
        var dt = new DataTransfer();
        dt.items.add(dropped[0]);
        input.files = dt.files;
      } catch (err) {
        // Older browsers: fall back to asking for a click.
        setError(zone, 'Плъзгането не се поддържа тук — натиснете полето и изберете снимката.');
        return;
      }
      showFile(zone, input, dropped[0]);
    });
  });

  // Stop double submits and show that something is happening.
  form.addEventListener('submit', function () {
    if (form.querySelector('.dz-error')) return;
    var btn = form.querySelector('button[type="submit"]');
    if (!btn) return;
    btn.disabled = true;
    btn.textContent = 'Качване…';
  });
});
