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
    // Attempt to refresh token on startup (cookie-based session)
    this.refresh().subscribe({
      next: () => {},
      error: () => this.userSubject.next(null)
    });
  }

  login(username: string, password: string = 'password') {
    return this.http.post<any>(`${this.apiUrl}/login`, { username, password }, { withCredentials: true })
      .pipe(
        tap(user => this.userSubject.next(user))
      );
  }

  refresh() {
    return this.http.post<any>(`${this.apiUrl}/refresh`, {}, { withCredentials: true })
      .pipe(
        tap(user => this.userSubject.next(user)),
        catchError(() => {
          this.userSubject.next(null);
          return of(null);
        })
      );
  }

  getProfile() {
    return this.http.get<any>(`${this.apiUrl}/profile`, { withCredentials: true });
  }

  logout() {
    this.http.post(`${this.apiUrl}/logout`, {}, { withCredentials: true }).subscribe({
      next: () => this.userSubject.next(null),
      error: () => this.userSubject.next(null)
    });
  }

  isLoggedIn(): boolean {
    return !!this.userSubject.value;
  }

  getUser() {
    return this.userSubject.value;
  }
}
