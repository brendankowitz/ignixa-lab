export function scrollTransform(scrollLeft: number, scrollTop: number): string {
  return `translate(${-scrollLeft}px, ${-scrollTop}px)`;
}
