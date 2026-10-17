import { Component, Input } from '@angular/core';

@Component({
  selector: 'app-icon',
  template: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path [attr.d]="paths[name] || paths[\'music\']" /></svg>',
  styles: ':host{display:inline-flex;width:1.25em;height:1.25em;flex-shrink:0}svg{width:100%;height:100%}',
})
export class Icon {
  @Input() name = 'music';
  protected readonly paths: Record<string, string> = {
    download: 'M12 3v12m-5-5 5 5 5-5M4 16v4a1 1 0 0 0 1 1h14a1 1 0 0 0 1-1v-4',
    arrow: 'M5 12h14m-6-6 6 6-6 6',
    chevron: 'm7 10 5 5 5-5',
    menu: 'M4 7h16M4 12h16M4 17h16',
    close: 'm6 6 12 12M6 18 18 6',
    headphones: 'M4 14v-3a8 8 0 0 1 16 0v3M4 12H3v8h4v-8H4m16 0h1v8h-4v-8h3',
    collection: 'M4 7h16v14H4zM6 3h12M5 5h14M10 11v6l5-3-5-3',
    offline: 'm3 3 18 18M5 7a13 13 0 0 0-3 2m7-4a14 14 0 0 1 13 4M7 12a8 8 0 0 0-2 1m9-3a8 8 0 0 1 5 3M9 17a4 4 0 0 1 6 0M12 21h.01',
    music: 'M9 18V5l11-2v13M9 9l11-2M9 18c0 1.7-1.8 3-4 3s-3-1-3-2 1.8-3 4-3c1.2 0 2.3.4 3 1m11-1c0 1.7-1.8 3-4 3s-3-1-3-2 1.8-3 4-3c1.2 0 2.3.4 3 1',
    check: 'm5 12 4 4L19 6',
    play: 'm9 5 11 7-11 7V5',
    heart: 'M20.8 4.6a5.4 5.4 0 0 0-7.6 0L12 5.8l-1.2-1.2a5.4 5.4 0 0 0-7.6 7.6L12 21l8.8-8.8a5.4 5.4 0 0 0 0-7.6',
    android: 'M5 9h14v10H5zM8 5 6 2m10 3 2-3M5 9a7 7 0 0 1 14 0M2 11v6m20-6v6M8 19v3m8-3v3M9 6h.01M15 6h.01',
    moon: 'M20 14a9 9 0 0 1-10-10 9 9 0 1 0 10 10',
    sun: 'M12 3V1m0 22v-2M3 12H1m22 0h-2M5 5 3 3m18 18-2-2M5 19l-2 2M21 3l-2 2M16 12a4 4 0 1 1-8 0 4 4 0 0 1 8 0',
    spark: 'm12 2 2.8 7.2L22 12l-7.2 2.8L12 22l-2.8-7.2L2 12l7.2-2.8L12 2',
  };
}
