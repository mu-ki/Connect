import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { BehaviorSubject, tap } from 'rxjs';

@Injectable({
  providedIn: 'root'
})
export class AuthService {
  // Use relative API paths so the app works from a virtual directory like /Connect
  private apiUrl = 'api/auth';
  private tokenSubject = new BehaviorSubject<string | null>(localStorage.getItem('token'));
  public token$ = this.tokenSubject.asObservable();

  constructor(private http: HttpClient) { }

  login(username: string, password: string = 'password') {
    return this.http.post<{ token: string, user: any }>(`${this.apiUrl}/login`, { username, password })
      .pipe(
        tap(response => {
          localStorage.setItem('token', response.token);
          this.tokenSubject.next(response.token);
        })
      );
  }

  logout() {
    localStorage.removeItem('token');
    this.tokenSubject.next(null);
  }

  getToken(): string | null {
    return this.tokenSubject.value;
  }

  searchUsers(query: string) {
    const token = this.getToken();
    const headers = { Authorization: `Bearer ${token}` };
    return this.http.get<any[]>(`${this.apiUrl}/search?q=${encodeURIComponent(query)}`, { headers });
  }

  getOnlineUsers() {
    const token = this.getToken();
    const headers = { Authorization: `Bearer ${token}` };
    return this.http.get<string[]>(`${this.apiUrl}/online`, { headers });
  }
}
