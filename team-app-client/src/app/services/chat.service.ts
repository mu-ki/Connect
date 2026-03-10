import { Injectable } from '@angular/core';
import { HttpClient, HttpHeaders } from '@angular/common/http';
import * as signalR from '@microsoft/signalr';
import { AuthService } from './auth.service';
import { BehaviorSubject, Subject } from 'rxjs';

@Injectable({
    providedIn: 'root'
})
export class ChatService {
    private hubConnection: signalR.HubConnection | null = null;
    // Use relative paths so the app works from a virtual directory (e.g., /Connect)
    private apiUrl = 'api/chat';
    private hubUrl = 'chathub';

    private messagesSubject = new BehaviorSubject<any[]>([]);
    public messages$ = this.messagesSubject.asObservable();

    private userOnlineSubject = new Subject<string>();
    public userOnline$ = this.userOnlineSubject.asObservable();

    private userOfflineSubject = new Subject<string>();
    public userOffline$ = this.userOfflineSubject.asObservable();

    public get hubConnectionRef() {
        return this.hubConnection;
    }

    constructor(private http: HttpClient, private authService: AuthService) { }

    public getHeaders() {
        const token = this.authService.getToken();
        return new HttpHeaders({ 'Authorization': `Bearer ${token}` });
    }

    public async startConnection() {
        const token = this.authService.getToken();
        if (!token) return;

        this.hubConnection = new signalR.HubConnectionBuilder()
            .withUrl(this.hubUrl, { accessTokenFactory: () => token })
            .withAutomaticReconnect()
            .build();

        this.hubConnection.on('ReceiveMessage', (message) => {
            const currentMessages = this.messagesSubject.value;
            this.messagesSubject.next([...currentMessages, message]);
        });

        this.hubConnection.on('UserOnline', (upn: string) => {
            this.userOnlineSubject.next(upn);
        });

        this.hubConnection.on('UserOffline', (upn: string) => {
            this.userOfflineSubject.next(upn);
        });

        try {
            await this.hubConnection.start();
            console.log('SignalR connected');
        } catch (err) {
            console.error('SignalR connection error: ', err);
        }
    }

    public getChannels() {
        return this.http.get<any[]>(`${this.apiUrl}/channels`, { headers: this.getHeaders() });
    }

    public getChannelMessages(channelId: string) {
        return this.http.get<any[]>(`${this.apiUrl}/channels/${channelId}/messages`, { headers: this.getHeaders() })
            .subscribe(messages => {
                this.messagesSubject.next(messages);
            });
    }

    public getOrCreateDm(targetUpn: string, targetDisplayName: string) {
        return this.http.post<any>(`${this.apiUrl}/dm`, { targetUpn, targetDisplayName }, { headers: this.getHeaders() });
    }

    public async joinChannel(channelId: string) {
        if (this.hubConnection?.state === signalR.HubConnectionState.Connected) {
            await this.hubConnection.invoke('JoinChannel', channelId);
        }
    }

    public async leaveChannel(channelId: string) {
        if (this.hubConnection?.state === signalR.HubConnectionState.Connected) {
            await this.hubConnection.invoke('LeaveChannel', channelId);
        }
    }

    public async sendMessage(channelId: string, content: string) {
        if (this.hubConnection?.state === signalR.HubConnectionState.Connected) {
            await this.hubConnection.invoke('SendMessageToChannel', channelId, content);
        }
    }

    public getProfile() {
        return this.http.get<any>(`${this.apiUrl}/profile`, { headers: this.getHeaders() });
    }

    public uploadAvatar(file: File) {
        const formData = new FormData();
        formData.append('file', file);
        const token = this.authService.getToken();
        const headers = new HttpHeaders({ 'Authorization': `Bearer ${token}` });
        return this.http.post<{ avatarUrl: string }>(`${this.apiUrl}/profile/avatar`, formData, { headers });
    }

    public getUsers() {
        return this.http.get<{ displayName: string, avatarUrl: string | null }[]>(`${this.apiUrl}/users`, { headers: this.getHeaders() });
    }
}
