import { Component, ElementRef, HostListener, signal, viewChild } from '@angular/core';
import { Icon } from './icon';
import { release } from './release.generated';

@Component({
  selector: 'app-root',
  imports: [Icon],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {
  protected readonly release = release;
  protected readonly fileSize = release.fileSizeBytes ? (release.fileSizeBytes / 1_000_000).toFixed(1) + ' MB' : null;
  protected readonly menuOpen = signal(false);
  protected readonly selectedScreen = signal(0);
  private readonly privacyDialog = viewChild.required<ElementRef<HTMLDialogElement>>('privacyDialog');
  protected readonly screens = [
    { name: 'Your library', image: 'images/app-library.png', title: 'All your favorites. One little home.',
      description: 'Your saved songs, a quick search, and collections within reach. Hit shuffle and let the day find its rhythm.',
      alt: 'Nar Htaung Lite home screen with downloaded songs and collections', icon: 'music' },
    { name: 'Collections', image: 'images/app-collections.png', title: 'A collection for every version of you.',
      description: 'For late nights, slow mornings, and the long way home. Give your favorite songs a place of their own.',
      alt: 'Nar Htaung Lite collections screen with a sample music library', icon: 'collection' },
    { name: 'Now playing', image: 'images/app-player.png', title: 'Just you and a really good song.',
      description: 'Artwork up front. Playback controls right where you need them. Keep the music going from the mini player or your notifications.',
      alt: 'Nar Htaung Lite full-screen player with cover artwork and playback controls', icon: 'headphones' },
  ];
  protected readonly faqs = [
    { question: 'Does it work without an internet connection?', answer: 'Yes. Once you download a track, you can play it and use your saved collections offline. Searching for new music and downloading tracks needs an internet connection.' },
    { question: 'Which phones can run it?', answer: 'This download is for 64-bit Android phones running Android ' + release.minimumAndroidVersion + ' or later. An iPhone download is not available yet.' },
    { question: 'What is an APK, and how do I install it?', answer: 'An APK is the installation file for an Android app. Download it, open it from your browser or Downloads folder, and follow Android’s installation prompts. Android may ask you to allow installation from the browser you used.' },
    { question: 'Can I make my own collections?', answer: 'Absolutely. All downloads keeps every saved song together. You can create your own collections, add songs you already have, and shuffle any collection.' },
    { question: 'Do I need an account?', answer: 'No account is needed for the current app. Your downloaded music and collections are stored on your device.' },
  ];
  protected selectScreen(index: number): void { this.selectedScreen.set(index); }
  protected navigateScreens(event: KeyboardEvent, index: number): void {
    let next: number;
    switch (event.key) {
      case 'ArrowRight': next = (index + 1) % this.screens.length; break;
      case 'ArrowLeft': next = (index - 1 + this.screens.length) % this.screens.length; break;
      case 'Home': next = 0; break;
      case 'End': next = this.screens.length - 1; break;
      default: return;
    }
    event.preventDefault();
    this.selectedScreen.set(next);
    document.getElementById('screen-tab-' + next)?.focus();
  }
  protected closeMenu(): void { this.menuOpen.set(false); }
  @HostListener('document:keydown.escape') onEscape(): void { this.closeMenu(); }
  protected openPrivacy(): void { this.privacyDialog().nativeElement.showModal(); }
  protected closePrivacy(): void { this.privacyDialog().nativeElement.close(); }
  protected dismissBackdrop(event: MouseEvent): void { if (event.target === event.currentTarget) this.closePrivacy(); }
}
