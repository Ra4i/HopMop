document.addEventListener('DOMContentLoaded', ()=>{
  document.querySelectorAll('.ba-slider').forEach(initSlider);
});
function initSlider(el){
  const before = el.querySelector('.before');
  const after = el.querySelector('.after');
  const handle = el.querySelector('.slider-handle');
  let dragging = false;
  function update(x){
    const rect = el.getBoundingClientRect();
    const pct = Math.max(0,Math.min(1,(x - rect.left)/rect.width));
    after.style.clipPath = `inset(0 ${100 - pct*100}% 0 0)`;
    handle.style.left = `${pct*100}%`;
  }
  el.addEventListener('mousedown', e=>{ dragging=true; update(e.clientX); });
  window.addEventListener('mouseup', ()=>dragging=false);
  window.addEventListener('mousemove', e=>{ if(dragging) update(e.clientX); });
  // touch
  el.addEventListener('touchstart', e=>{ update(e.touches[0].clientX); dragging=true; });
  window.addEventListener('touchend', ()=>dragging=false);
  window.addEventListener('touchmove', e=>{ if(dragging) update(e.touches[0].clientX); });
}
