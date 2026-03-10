import { Component } from '@angular/core';
import { Router } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { AuthService } from '../../services/auth.service';

@Component({
    selector: 'app-login',
    standalone: true,
    imports: [FormsModule],
    templateUrl: './login.component.html',
    styleUrl: './login.component.css'
})
export class LoginComponent {
    username = '';
    password = '';
    error = '';

    constructor(private authService: AuthService, private router: Router) { }

    login() {
        this.error = '';
        if (!this.username) return;

        this.authService.login(this.username, this.password).subscribe({
            next: () => {
                this.router.navigate(['/chat']);
            },
            error: (err) => {
                this.error = 'Invalid Active Directory credentials.';
            }
        });
    }
}
