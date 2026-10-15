const minute = 60_000;
export function timeBounds(points) {
  if (!points.length) return null;
  const start = Date.parse(points[0].time), last = Date.parse(points.at(-1).time);
  return {start, end: Math.max(start + minute, last)};
}
export function clampWindow(window, bounds) {
  const span = Math.min(bounds.end - bounds.start, Math.max(minute, window.end - window.start));
  const start = Math.max(bounds.start, Math.min(window.start, bounds.end - span));
  return {start, end: start + span};
}
export function zoomWindow(window, bounds, anchor, factor) {
  const ratio = Math.max(0, Math.min(1, (anchor - window.start) / (window.end - window.start)));
  const span = Math.max(minute, Math.min(bounds.end - bounds.start, (window.end - window.start) * factor));
  return clampWindow({start: anchor - span * ratio, end: anchor + span * (1 - ratio)}, bounds);
}
export function selectWindow(first, last, bounds) {
  return clampWindow({start: Math.min(first, last), end: Math.max(first, last)}, bounds);
}
export function pointsInWindow(points, window) {
  return points.filter(p => {const t = Date.parse(p.time); return t >= window.start && t <= window.end;});
}

// Pointer capture keeps a drag active outside the SVG. Only the horizontal
// interval changes; the renderer derives price limits from visible observations.
export function bindChartNavigation(container, {getState, onWindow, onHover, onLeave}) {
  let drag = null;
  function location(event) {
    const {layout} = getState(), svg = container.querySelector('svg');
    if (!layout || !svg) return null;
    const rect = svg.getBoundingClientRect(), matrix = svg.getScreenCTM?.(), point = svg.createSVGPoint?.();
    if (point) {point.x = event.clientX; point.y = event.clientY;}
    const local = matrix && point ? point.matrixTransform(matrix.inverse()) : null;
    const raw = local?.x ?? (event.clientX - rect.left) / rect.width * layout.w;
    const y = local?.y ?? (event.clientY - rect.top) / rect.height * layout.h;
    const x = Math.max(layout.left, Math.min(layout.w - layout.right, raw));
    return {x, inside: raw >= layout.left && raw <= layout.w - layout.right && y >= layout.top && y <= layout.h - layout.bottom,
      time: layout.first + (x - layout.left) / (layout.w - layout.left - layout.right) * layout.span};
  }
  function clearDrag() {
    const previous = drag;
    drag = null;
    if (previous && container.hasPointerCapture(previous.id)) container.releasePointerCapture(previous.id);
    container.querySelector('.chart-selection')?.setAttribute('hidden', '');
    container.classList.remove('selecting');
  }
  container.addEventListener('pointerdown', event => {
    if (!event.isPrimary || event.button !== 0) return;
    const position = location(event);
    if (!position?.inside) return;
    container.setPointerCapture(event.pointerId);
    drag = {id: event.pointerId, start: position};
    container.classList.add('selecting');
  });
  container.addEventListener('pointermove', event => {
    const position = location(event);
    if (!position) return;
    if (drag && drag.id === event.pointerId) {
      const selection = container.querySelector('.chart-selection');
      selection?.removeAttribute('hidden');
      selection?.setAttribute('x', Math.min(drag.start.x, position.x));
      selection?.setAttribute('width', Math.abs(position.x - drag.start.x));
    } else if (position.inside) onHover(position.time);
    else onLeave();
  });
  container.addEventListener('pointerup', event => {
    if (!drag || drag.id !== event.pointerId) return;
    const position = location(event), start = drag.start, {bounds} = getState();
    clearDrag();
    if (position && bounds && Math.abs(position.x - start.x) >= 8) onWindow(selectWindow(start.time, position.time, bounds));
  });
  container.addEventListener('pointercancel', clearDrag);
  container.addEventListener('lostpointercapture', clearDrag);
  container.addEventListener('pointerleave', () => {if (!drag) onLeave();});
  container.addEventListener('wheel', event => {
    if (drag) return;
    const position = location(event), {window, bounds} = getState();
    if (!position?.inside || !window || !bounds) return;
    // Consume scrolling only over the plot, so the rest of the page still scrolls.
    event.preventDefault();
    const delta = (event.deltaY || event.deltaX) * (event.deltaMode === 1 ? 16 : event.deltaMode === 2 ? 390 : 1);
    onWindow(zoomWindow(window, bounds, position.time, Math.exp(Math.max(-1, Math.min(1, delta * .002)))));
  }, {passive: false});
  return clearDrag;
}
