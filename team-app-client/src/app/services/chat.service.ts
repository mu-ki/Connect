import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import * as signalR from '@microsoft/signalr';
import { BehaviorSubject, Observable, Subject, tap } from 'rxjs';

export interface ConversationMember {
    id: string;
    adUpn: string;
    displayName: string;
    avatarUrl: string | null;
}

export interface ConversationSummary {
    id: string;
    name: string;
    type: 'Direct' | 'Group';
    isGroup: boolean;
    memberCount: number;
    members: ConversationMember[];
    otherParticipantUpn?: string | null;
    otherParticipantName?: string | null;
    lastMessageAt?: string | null;
}

@Injectable({
    providedIn: 'root'
})
export class ChatService {
    private hubConnection: signalR.HubConnection | null = null;
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

    constructor(private http: HttpClient) { }

    public async startConnection() {
        if (this.hubConnection) {
            return;
        }

        this.hubConnection = new signalR.HubConnectionBuilder()
            .withUrl(this.hubUrl, { withCredentials: true })
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

    public getConversations() {
        return this.http.get<ConversationSummary[]>(`${this.apiUrl}/conversations`, { withCredentials: true });
    }

    public getConversationMessages(conversationId: string): Observable<any[]> {
        return this.http.get<any[]>(`${this.apiUrl}/conversations/${conversationId}/messages`, { withCredentials: true })
            .pipe(tap(messages => this.messagesSubject.next(messages)));
    }

    public getOrCreateDirectConversation(targetUpn: string, targetDisplayName: string) {
        return this.http.post<ConversationSummary>(`${this.apiUrl}/conversations/direct`, { targetUpn, targetDisplayName }, { withCredentials: true });
    }

    public createGroup(name: string, members: { adUpn: string, displayName?: string }[]) {
        return this.http.post<ConversationSummary>(`${this.apiUrl}/groups`, { name, members }, { withCredentials: true });
    }

    public async joinConversation(conversationId: string) {
        if (this.hubConnection?.state === signalR.HubConnectionState.Connected) {
            await this.hubConnection.invoke('JoinConversation', conversationId);
        }
    }

    public async leaveConversation(conversationId: string) {
        if (this.hubConnection?.state === signalR.HubConnectionState.Connected) {
            await this.hubConnection.invoke('LeaveConversation', conversationId);
        }
    }

    public async sendMessage(conversationId: string, content: string) {
        if (this.hubConnection?.state === signalR.HubConnectionState.Connected) {
            await this.hubConnection.invoke('SendMessageToConversation', conversationId, content);
        }
    }

    public uploadAvatar(file: File) {
        const formData = new FormData();
        formData.append('file', file);
        return this.http.post<{ avatarUrl: string }>(`${this.apiUrl}/profile/avatar`, formData, { withCredentials: true });
    }

    public getUsers() {
        return this.http.get<{ adUpn: string, displayName: string, avatarUrl: string | null }[]>(`${this.apiUrl}/users`, { withCredentials: true });
    }

    public getOnlineUsers() {
        return this.http.get<string[]>(`${this.apiUrl}/online`, { withCredentials: true });
    }

    public searchUsers(query: string) {
        return this.http.get<any[]>(`${this.apiUrl}/search?q=${encodeURIComponent(query)}`, { withCredentials: true });
    }
}
