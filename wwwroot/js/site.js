// Progressive visual enhancements. Everything here is optional polish —
// the site renders and works identically with JS disabled.
// Confirmation for destructive forms. This lives here rather than in an
// onsubmit="" attribute because the Content-Security-Policy sets
// script-src 'self', which blocks inline handlers. One delegated listener
// covers every current and future form that carries data-confirm.
document.addEventListener('submit', function (e) {
  var form = e.target.closest('form[data-confirm]');
  if (form && !window.confirm(form.getAttribute('data-confirm'))) {
    e.preventDefault();
  }
}, true);

document.addEventListener('DOMContentLoaded', function () {
  var reduceMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;

  // 1. Header gains a deeper shadow once the page is scrolled.
  var header = document.querySelector('.site-header');
  if (header) {
    var syncHeader = function () {
      header.classList.toggle('scrolled', window.scrollY > 8);
    };
    syncHeader();
    window.addEventListener('scroll', syncHeader, { passive: true });
  }

  // 2. Fade sections in as they enter the viewport.
  //    The .js-reveal flag is only set when we can actually observe them,
  //    so without IntersectionObserver nothing is ever hidden.
  if (reduceMotion || !('IntersectionObserver' in window)) return;

  var targets = document.querySelectorAll('.card, .pair, .gallery-preview, .seo-block, .contact-info');
  if (!targets.length) return;

  document.documentElement.classList.add('js-reveal');

  var observer = new IntersectionObserver(function (entries) {
    entries.forEach(function (entry) {
      if (!entry.isIntersecting) return;
      entry.target.classList.add('in');
      observer.unobserve(entry.target);
    });
  }, { rootMargin: '0px 0px -8% 0px', threshold: 0.08 });

  targets.forEach(function (el, i) {
    el.classList.add('reveal');
    el.style.transitionDelay = Math.min(i, 5) * 60 + 'ms';
    observer.observe(el);
  });
});
