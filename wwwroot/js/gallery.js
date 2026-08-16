/* Before / After gallery.
   Two independent pieces, both count-agnostic — everything is driven by
   querySelectorAll, so 2, 3, 4 or 20 cards behave identically:

     1. initSlider          — one self-contained comparison slider per card
     2. initExpandableGrid  — click a card to focus it, the siblings shrink

   A drag on a slider never reaches the card's expand handler (see the
   capture-phase click guard in initSlider). */
(function () {
  'use strict';

  // How far a press may travel before it counts as a drag instead of a click.
  var DRAG_SLOP = 5;
  // A drag that ends outside its slider still produces a click on the card,
  // and pointer capture means that click can be retargeted past the slider's
  // own guard. This is the second line of defence for that case.
  var DRAG_GUARD_MS = 300;
  var lastDragEndAt = 0;
  // Below this the shrunken cards stop being readable, so the row stacks
  // instead. That is what keeps 8 or 20 cards sane without hard-coding a count.
  var MIN_CARD_WIDTH = 130;

  document.addEventListener('DOMContentLoaded', function () {
    each(document.querySelectorAll('.ba-slider'), initSlider);
    each(document.querySelectorAll('.gallery-grid'), initExpandableGrid);
  });

  function each(list, fn) { Array.prototype.forEach.call(list, fn); }

  /* -----------------------------------------------------------------
     1. Before / After slider
     Each instance closes over its own element, so moving the divider in
     one card cannot touch any other card.
     ----------------------------------------------------------------- */
  function initSlider(el) {
    var after = el.querySelector('.after');
    var handle = el.querySelector('.slider-handle');
    if (!after || !handle) return;

    var pct = 50;
    var dragging = false;
    var moved = false;
    var startX = 0, startY = 0;
    var activeId = null;

    function setPct(next) {
      pct = Math.max(0, Math.min(100, next));
      // clip-path: inset(top right bottom left) — the LEFT inset has to match
      // the handle position so the seam and the divider stay glued together.
      // Both are percentages, so the slider survives any resize on its own.
      after.style.clipPath = 'inset(0 0 0 ' + pct + '%)';
      handle.style.left = pct + '%';
      el.setAttribute('aria-valuenow', String(Math.round(pct)));
    }

    function pctFromClientX(x) {
      var rect = el.getBoundingClientRect();
      if (!rect.width) return pct;
      return ((x - rect.left) / rect.width) * 100;
    }

    function start(x, y) {
      dragging = true;
      moved = false;
      startX = x;
      startY = y;
      el.classList.add('is-dragging');
      setPct(pctFromClientX(x));
    }

    function move(x, y) {
      if (!dragging) return;
      if (Math.abs(x - startX) > DRAG_SLOP || Math.abs(y - startY) > DRAG_SLOP) moved = true;
      setPct(pctFromClientX(x));
    }

    function end() {
      if (!dragging) return;
      dragging = false;
      if (moved) lastDragEndAt = Date.now();
      el.classList.remove('is-dragging');
    }

    // The click that follows a drag must not expand/collapse the card.
    // Capture phase + stopPropagation means it never reaches the grid.
    el.addEventListener('click', function (e) {
      if (!moved) return;
      moved = false;
      e.stopPropagation();
      e.preventDefault();
    }, true);

    if (window.PointerEvent) {
      // Pointer events cover mouse, touch and pen in one path.
      el.addEventListener('pointerdown', function (e) {
        if (e.button > 0) return;
        activeId = e.pointerId;
        try { el.setPointerCapture(e.pointerId); } catch (err) { /* not fatal */ }
        start(e.clientX, e.clientY);
        e.preventDefault();
      });
      el.addEventListener('pointermove', function (e) {
        if (e.pointerId === activeId) move(e.clientX, e.clientY);
      });
      el.addEventListener('pointerup', releasePointer);
      el.addEventListener('pointercancel', releasePointer);
    } else {
      // Fallback for browsers without Pointer Events: mouse + touch.
      el.addEventListener('mousedown', function (e) {
        if (e.button > 0) return;
        e.preventDefault();
        start(e.clientX, e.clientY);
        document.addEventListener('mousemove', onDocMouseMove);
        document.addEventListener('mouseup', onDocMouseUp);
      });
      el.addEventListener('touchstart', function (e) {
        var t = e.changedTouches[0];
        start(t.clientX, t.clientY);
      }, { passive: true });
      el.addEventListener('touchmove', function (e) {
        var t = e.changedTouches[0];
        move(t.clientX, t.clientY);
        if (dragging && e.cancelable) e.preventDefault(); // don't scroll mid-drag
      }, { passive: false });
      el.addEventListener('touchend', end);
      el.addEventListener('touchcancel', end);
    }

    function releasePointer(e) {
      if (e.pointerId !== activeId) return;
      try { el.releasePointerCapture(e.pointerId); } catch (err) { /* not fatal */ }
      activeId = null;
      end();
    }

    function onDocMouseMove(e) { move(e.clientX, e.clientY); }
    function onDocMouseUp() {
      end();
      document.removeEventListener('mousemove', onDocMouseMove);
      document.removeEventListener('mouseup', onDocMouseUp);
    }

    // Keyboard support for role="slider".
    el.addEventListener('keydown', function (e) {
      var step = e.shiftKey ? 10 : 2;
      if (e.key === 'ArrowLeft' || e.key === 'ArrowDown') setPct(pct - step);
      else if (e.key === 'ArrowRight' || e.key === 'ArrowUp') setPct(pct + step);
      else if (e.key === 'Home') setPct(0);
      else if (e.key === 'End') setPct(100);
      else return;
      e.preventDefault();
      e.stopPropagation();
    });

    setPct(50);
  }

  /* -----------------------------------------------------------------
     2. Expandable cards
     One delegated listener for the whole grid — no per-card handlers,
     no assumptions about how many cards there are.
     ----------------------------------------------------------------- */
  function initExpandableGrid(grid) {
    if (!grid.querySelector('.before-after-card')) return;

    var delaysCleared = false;
    var syncQueued = false;

    grid.classList.add('is-interactive');
    sync();
    window.addEventListener('resize', queueSync);

    function cards() { return grid.querySelectorAll('.before-after-card'); }

    function queueSync() {
      if (syncQueued) return;
      syncQueued = true;
      window.requestAnimationFrame(function () { syncQueued = false; sync(); });
    }

    function sync() {
      var n = cards().length;
      if (!n) return;
      // The focused card takes roughly half the row whatever the count is
      // — grow / (grow + n - 1) — capped so a long row keeps its siblings usable.
      grid.style.setProperty('--pair-grow', String(Math.min(3.4, Math.max(1.4, n - 0.6))));
      // Too many cards for the available width? Stack them instead of
      // squeezing everything into slivers.
      var width = grid.clientWidth;
      grid.classList.toggle('is-stacked', width > 0 && width / n < MIN_CARD_WIDTH);
    }

    function setState(card, expanded) {
      card.classList.toggle('is-expanded', expanded);
      var toggle = card.querySelector('.pair-toggle');
      if (toggle) toggle.setAttribute('aria-expanded', expanded ? 'true' : 'false');
    }

    function focusCard(card) {
      each(cards(), function (other) {
        if (other !== card) setState(other, false);
      });
      setState(card, true);
      grid.classList.add('has-expanded');
    }

    function collapseAll() {
      each(cards(), function (card) { setState(card, false); });
      grid.classList.remove('has-expanded');
    }

    // site.js staggers its scroll-reveal with an inline transition-delay,
    // which would also postpone the expand animation. Drop it the first time
    // a card is used — by then the reveal has long finished.
    function clearRevealDelays() {
      if (delaysCleared) return;
      delaysCleared = true;
      each(cards(), function (card) { card.style.transitionDelay = '0s'; });
    }

    grid.addEventListener('click', function (e) {
      var target = e.target;
      if (!target || !target.closest) return;

      var card = target.closest('.before-after-card');
      if (!card || !grid.contains(card)) return;
      // leave any real control inside a card alone
      if (target.closest('a,input,select,textarea,label,button:not(.pair-toggle)')) return;
      // a click that is really the tail of a slider drag never expands
      if (Date.now() - lastDragEndAt < DRAG_GUARD_MS) return;

      clearRevealDelays();

      if (card.classList.contains('is-expanded')) {
        // Only the title toggle collapses. A stray click on the photo of an
        // already-open card must not close it.
        if (target.closest('.pair-toggle')) collapseAll();
        return;
      }
      focusCard(card);
    });

    document.addEventListener('keydown', function (e) {
      if (e.key === 'Escape' && grid.classList.contains('has-expanded')) collapseAll();
    });
  }
})();
