import { Routes } from '@angular/router';


export const routes: Routes = [
    { path: '', redirectTo: '/login', pathMatch: 'full' },
    { path: 'login', loadComponent: () => import('./components/login/login.component').then(m => m.LoginComponent) },
    { path: 'chat', loadComponent: () => import('./components/chat/chat.component').then(m => m.ChatComponent) }
];
