document.addEventListener('DOMContentLoaded', ()=>{
  document.querySelectorAll('.ba-slider').forEach(initSlider);
});
function initSlider(el){
  const before = el.querySelector('.before');
  const after = el.querySelector('.after');
  const handle = el.querySelector('.slider-handle');
  let dragging = false;

  function updateFromClientX(x){
    const rect = el.getBoundingClientRect();
    const pct = Math.max(0, Math.min(1, (x - rect.left) / rect.width));
    // The clip boundary must match the handle position. clip-path: inset(top right bottom left)
    // so LEFT inset should equal the handle percentage across the element.
    const leftInset = pct * 100;
    after.style.clipPath = `inset(0 0 0 ${leftInset}%)`;
    handle.style.left = `${pct * 100}%`;
  }

  function onPointerDown(e){
    e.preventDefault();
    dragging = true;
    try { e.target.setPointerCapture(e.pointerId); } catch (err) {}
    updateFromClientX(e.clientX);
  }
  function onPointerMove(e){ if(dragging) updateFromClientX(e.clientX); }
  function onPointerUp(e){ dragging = false; try { e.target.releasePointerCapture(e.pointerId); } catch (err) {} }

  // Use pointer events on the handle for consistent touch/mouse behavior
  handle.addEventListener('pointerdown', onPointerDown);
  handle.addEventListener('pointermove', onPointerMove);
  handle.addEventListener('pointerup', onPointerUp);
  handle.addEventListener('pointercancel', ()=>{ dragging=false; });

  // Also allow interacting anywhere on the slider
  el.addEventListener('pointerdown', function(e){
    if(e.target === handle) return;
    e.preventDefault();
    dragging = true;
    try { el.setPointerCapture(e.pointerId); } catch (err) {}
    updateFromClientX(e.clientX);
  });
  el.addEventListener('pointermove', function(e){ if(dragging) updateFromClientX(e.clientX); });
  el.addEventListener('pointerup', function(e){ dragging = false; try { el.releasePointerCapture(e.pointerId); } catch (err) {} });

  // Initialize to center
  const initRect = el.getBoundingClientRect();
  updateFromClientX(initRect.left + initRect.width / 2);

  // Recalculate on resize while preserving handle percentage
  window.addEventListener('resize', ()=>{
    const pct = parseFloat(handle.style.left) || 50;
    const rect = el.getBoundingClientRect();
    updateFromClientX(rect.left + rect.width * (pct / 100));
  });
}
