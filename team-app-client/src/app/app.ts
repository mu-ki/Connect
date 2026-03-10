import { Component, signal, inject } from '@angular/core';
import { RouterOutlet, Router, RouteConfigLoadStart, RouteConfigLoadEnd } from '@angular/router';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet],
  templateUrl: './app.html',
  styleUrl: './app.css'
})
export class App {
  protected readonly title = signal('team-app-client');
  public isLoading = signal(false);
  private router = inject(Router);

  constructor() {
    this.router.events.subscribe(event => {
      if (event instanceof RouteConfigLoadStart) {
        this.isLoading.set(true);
      } else if (event instanceof RouteConfigLoadEnd) {
        this.isLoading.set(false);
      }
    });
  }
}
