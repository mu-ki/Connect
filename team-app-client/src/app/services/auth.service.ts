import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { BehaviorSubject, catchError, of, tap } from 'rxjs';

@Injectable({
  providedIn: 'root'
})
export class AuthService {
  private apiUrl = 'api/auth';
  private userSubject = new BehaviorSubject<any | null>(null);
  public user$ = this.userSubject.asObservable();

  constructor(private http: HttpClient) {
    // Seed with cached user (helps UI render correctly while refresh is running).
    const cached = localStorage.getItem('connect_user');
    if (cached) {
      try {
        this.userSubject.next(JSON.parse(cached));
      } catch {
        localStorage.removeItem('connect_user');
      }
    }

    // Attempt to refresh token on startup (cookie-based session)
    this.refresh().subscribe({
      next: () => {},
      error: () => {
        this.userSubject.next(null);
        localStorage.removeItem('connect_user');
      }
    });
  }

  login(username: string, password: string = 'password') {
    return this.http.post<any>(`${this.apiUrl}/login`, { username, password }, { withCredentials: true })
      .pipe(
        tap(user => {
          this.userSubject.next(user);
          localStorage.setItem('connect_user', JSON.stringify(user));
        })
      );
  }

  refresh() {
    return this.http.post<any>(`${this.apiUrl}/refresh`, {}, { withCredentials: true })
      .pipe(
        tap(user => {
          // Always update the user state with what the server returns.
          // If the refresh endpoint returns null, we should clear the cached user.
          this.userSubject.next(user);
          if (user) {
            localStorage.setItem('connect_user', JSON.stringify(user));
          } else {
            localStorage.removeItem('connect_user');
          }
        }),
        catchError(() => {
          this.userSubject.next(null);
          localStorage.removeItem('connect_user');
          return of(null);
        })
      );
  }

  getProfile() {
    return this.http.get<any>(`${this.apiUrl}/profile`, { withCredentials: true });
  }

  logout() {
    this.http.post(`${this.apiUrl}/logout`, {}, { withCredentials: true }).subscribe({
      next: () => {
        this.userSubject.next(null);
        localStorage.removeItem('connect_user');
      },
      error: () => {
        this.userSubject.next(null);
        localStorage.removeItem('connect_user');
      }
    });
  }

  isLoggedIn(): boolean {
    return !!this.userSubject.value;
  }

  getUser() {
    return this.userSubject.value;
  }
}
