import { Component, OnDestroy, OnInit, ElementRef, ViewChild, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Subject, debounceTime, distinctUntilChanged, switchMap, tap, of, firstValueFrom, finalize } from 'rxjs';
import { ChatService, ConversationSummary } from '../../services/chat.service';
import { AuthService } from '../../services/auth.service';
import { WebrtcService } from '../../services/webrtc.service';
import { Router } from '@angular/router';

@Component({
    selector: 'app-chat',
    standalone: true,
    imports: [CommonModule, FormsModule],
    templateUrl: './chat.component.html',
    styleUrl: './chat.component.css'
})
export class ChatComponent implements OnInit, OnDestroy {
    directConversations: ConversationSummary[] = [];
    groupConversations: ConversationSummary[] = [];
    activeConversation: ConversationSummary | null = null;
    messages: any[] = [];
    newMessage = '';
    currentUser: string | null = '';
    currentUpn: string | null = '';

    onlineUsers = new Set<string>();

    searchQuery = '';
    searchResults: any[] = [];
    isSearching = false;
    private searchTerms = new Subject<string>();

    isGroupCreatorOpen = false;
    newGroupName = '';
    groupMembers: any[] = [];

    isCallActive = false;
    incomingCall: { callerUpn: string, callerName: string, isVideo: boolean } | null = null;

    @ViewChild('localVideo') localVideoRef!: ElementRef<HTMLVideoElement>;
    @ViewChild('remoteVideo') remoteVideoRef!: ElementRef<HTMLVideoElement>;

    currentUserAvatarUrl: string | null = null;
    userAvatarMap: Record<string, string | null> = {};
    userDisplayNameMap: Record<string, string> = {};
    @ViewChild('avatarFileInput') avatarFileInputRef!: ElementRef<HTMLInputElement>;

    constructor(
        public chatService: ChatService,
        private authService: AuthService,
        private webrtcService: WebrtcService,
        private router: Router,
        private cdr: ChangeDetectorRef
    ) { }

    private hasCheckedSession = false;
    public isSessionReady = false;

    async ngOnInit() {
        const cached = localStorage.getItem('connect_user');
        if (cached) {
            try {
                const cachedUser = JSON.parse(cached);
                this.currentUser = cachedUser.displayName || cachedUser.email || cachedUser.adUpn;
                this.currentUpn = cachedUser.adUpn || cachedUser.email;
            } catch {
                localStorage.removeItem('connect_user');
            }
        }

        this.authService.user$.subscribe(user => {
            if (!this.hasCheckedSession) return;

            if (!user) {
                this.router.navigate(['/login']);
                return;
            }

            this.currentUser = user.displayName || user.email || user.adUpn;
            this.currentUpn = user.adUpn || user.email;
        });

        this.searchTerms.pipe(
            debounceTime(300),
            distinctUntilChanged(),
            tap(() => {
                this.isSearching = true;
                this.searchResults = [];
            }),
            switchMap(term => term ? this.chatService.searchUsers(term) : of([]))
        ).subscribe({
            next: results => {
                this.searchResults = results;
                this.isSearching = false;
            },
            error: () => {
                this.searchResults = [];
                this.isSearching = false;
            }
        });

        await firstValueFrom(this.authService.refresh().pipe(
            finalize(() => {
                this.hasCheckedSession = true;
                this.isSessionReady = true;
            })
        ));

        this.loadUserProfiles();
        await this.chatService.startConnection();

        if (this.chatService.hubConnectionRef) {
            this.webrtcService.setHubConnection(this.chatService.hubConnectionRef);
        }

        this.chatService.getOnlineUsers().subscribe(users => {
            users.forEach(u => this.onlineUsers.add(u.toLowerCase()));
        });

        this.chatService.userOnline$.subscribe(upn => {
            this.onlineUsers.add(upn.toLowerCase());
            const user = this.searchResults.find(u => u.adUpn.toLowerCase() === upn.toLowerCase());
            if (user) {
                user.isOnline = true;
            }
        });

        this.chatService.userOffline$.subscribe(upn => {
            this.onlineUsers.delete(upn.toLowerCase());
            const user = this.searchResults.find(u => u.adUpn.toLowerCase() === upn.toLowerCase());
            if (user) {
                user.isOnline = false;
                user.lastSeen = new Date().toISOString();
            }
        });

        this.webrtcService.onIncomingCall.subscribe(data => {
            this.incomingCall = data;
        });

        this.webrtcService.onRemoteStreamAdd.subscribe(stream => {
            if (this.remoteVideoRef?.nativeElement) {
                this.remoteVideoRef.nativeElement.srcObject = stream;
            }
        });

        this.webrtcService.onCallEnded.subscribe(() => {
            this.isCallActive = false;
            this.incomingCall = null;
        });

        this.loadConversations();

        this.chatService.messages$.subscribe(msgs => {
            this.messages = msgs;
            this.scrollToBottom();
        });
    }

    loadConversations() {
        this.chatService.getConversations().subscribe(conversations => {
            this.directConversations = conversations.filter(c => c.type === 'Direct');
            this.groupConversations = conversations.filter(c => c.type === 'Group');

            const activeId = this.activeConversation?.id;
            const nextActive = conversations.find(c => c.id === activeId) ?? conversations[0] ?? null;
            if (nextActive) {
                this.selectConversation(nextActive);
            } else {
                this.activeConversation = null;
                this.messages = [];
            }
        });
    }

    async selectConversation(conversation: ConversationSummary) {
        if (this.activeConversation?.id === conversation.id) {
            return;
        }

        if (this.activeConversation) {
            await this.chatService.leaveConversation(this.activeConversation.id);
        }

        this.activeConversation = conversation;
        await this.chatService.joinConversation(conversation.id);
        this.chatService.getConversationMessages(conversation.id).subscribe();
    }

    displayConversationName(conversation: ConversationSummary | null): string {
        if (!conversation) return '';
        if (conversation.type === 'Direct') {
            return conversation.otherParticipantName || this.formatUpn(conversation.otherParticipantUpn || conversation.name);
        }
        return conversation.name;
    }

    conversationSubtitle(conversation: ConversationSummary | null): string {
        if (!conversation) return '';
        if (conversation.type === 'Direct') {
            return this.isConversationOnline(conversation) ? 'Online' : 'Offline';
        }

        const others = Math.max((conversation.memberCount || 1) - 1, 0);
        return `${others} other ${others === 1 ? 'member' : 'members'}`;
    }

    isConversationOnline(conversation: ConversationSummary | null): boolean {
        const targetUpn = conversation?.otherParticipantUpn;
        return !!targetUpn && this.onlineUsers.has(targetUpn.toLowerCase());
    }

    formatUpn(value: string): string {
        const namePart = value.split('@')[0];
        return namePart.split('.').map(s => s.charAt(0).toUpperCase() + s.slice(1)).join(' ');
    }

    onSearchChange() {
        this.searchTerms.next(this.searchQuery || '');
    }

    startDirectConversation(targetUser: any) {
        this.searchQuery = '';
        this.searchResults = [];
        this.chatService.getOrCreateDirectConversation(targetUser.adUpn, targetUser.displayName).subscribe(conversation => {
            const existing = this.directConversations.find(c => c.id === conversation.id);
            if (!existing) {
                this.directConversations = [conversation, ...this.directConversations];
            }
            this.selectConversation(conversation);
        });
    }

    toggleGroupCreator() {
        this.isGroupCreatorOpen = !this.isGroupCreatorOpen;
        if (!this.isGroupCreatorOpen) {
            this.resetGroupCreator();
        }
    }

    addGroupMember(user: any) {
        if (this.groupMembers.some(member => member.adUpn.toLowerCase() === user.adUpn.toLowerCase())) {
            return;
        }

        this.groupMembers = [...this.groupMembers, user];
        this.searchQuery = '';
        this.searchResults = [];
    }

    removeGroupMember(adUpn: string) {
        this.groupMembers = this.groupMembers.filter(member => member.adUpn !== adUpn);
    }

    createGroup() {
        if (!this.newGroupName.trim() || this.groupMembers.length === 0) {
            return;
        }

        const members = this.groupMembers.map(member => ({
            adUpn: member.adUpn,
            displayName: member.displayName
        }));

        this.chatService.createGroup(this.newGroupName.trim(), members).subscribe(group => {
            this.groupConversations = [group, ...this.groupConversations];
            this.resetGroupCreator();
            this.selectConversation(group);
        });
    }

    canCreateGroup(): boolean {
        return !!this.newGroupName.trim() && this.groupMembers.length > 0;
    }

    groupCreateHint(): string {
        if (!this.newGroupName.trim()) {
            return 'Enter a group name.';
        }

        if (this.groupMembers.length === 0) {
            return 'Add at least one member from search to enable Save Group.';
        }

        return `${this.groupMembers.length} member${this.groupMembers.length === 1 ? '' : 's'} selected.`;
    }

    async sendMessage() {
        if (!this.newMessage.trim() || !this.activeConversation) return;
        await this.chatService.sendMessage(this.activeConversation.id, this.newMessage);
        this.newMessage = '';
    }

    logout() {
        this.authService.logout();
        this.router.navigate(['/login']);
    }

    loadUserProfiles() {
        this.chatService.getUsers().subscribe(users => {
            users.forEach(u => {
                this.userAvatarMap[u.displayName] = u.avatarUrl || null;
                this.userDisplayNameMap[u.adUpn.toLowerCase()] = u.displayName;
            });
            if (this.currentUser) {
                this.currentUserAvatarUrl = this.userAvatarMap[this.currentUser] || null;
            }
        });
    }

    triggerAvatarUpload() {
        this.avatarFileInputRef.nativeElement.click();
    }

    onAvatarFileSelected(event: Event) {
        const input = event.target as HTMLInputElement;
        if (!input.files || input.files.length === 0) return;
        const file = input.files[0];
        this.chatService.uploadAvatar(file).subscribe({
            next: (res) => {
                const url = res.avatarUrl + '?t=' + Date.now();
                this.currentUserAvatarUrl = url;
                if (this.currentUser) {
                    this.userAvatarMap[this.currentUser] = url;
                }
                this.cdr.detectChanges();
            },
            error: (err) => console.error('Avatar upload failed', err)
        });
        input.value = '';
    }

    ngOnDestroy() {
        if (this.activeConversation) {
            this.chatService.leaveConversation(this.activeConversation.id);
        }
    }

    private resetGroupCreator() {
        this.isGroupCreatorOpen = false;
        this.newGroupName = '';
        this.groupMembers = [];
        this.searchQuery = '';
        this.searchResults = [];
    }

    private scrollToBottom() {
        setTimeout(() => {
            const container = document.querySelector('.chat-history');
            if (container) {
                container.scrollTop = container.scrollHeight;
            }
        }, 50);
    }

    startCall(isVideo: boolean) {
        if (!this.activeConversation || this.activeConversation.type !== 'Direct' || !this.activeConversation.otherParticipantUpn) {
            alert('Calling is only supported in one-to-one conversations.');
            return;
        }

        this.webrtcService.initiateCall(this.activeConversation.otherParticipantUpn, isVideo);
        this.isCallActive = true;
        this.attachLocalStream();
    }

    acceptCall() {
        if (!this.incomingCall) return;
        this.webrtcService.answerCall(this.incomingCall.callerUpn, this.incomingCall.isVideo);
        this.isCallActive = true;
        this.incomingCall = null;
        this.attachLocalStream();
    }

    rejectCall() {
        if (!this.incomingCall) return;
        this.webrtcService.declineCall(this.incomingCall.callerUpn);
        this.incomingCall = null;
    }

    endCall() {
        this.webrtcService.endCall();
        this.isCallActive = false;
    }

    private attachLocalStream() {
        setTimeout(() => {
            if (this.localVideoRef?.nativeElement && this.webrtcService.localStream) {
                this.localVideoRef.nativeElement.srcObject = this.webrtcService.localStream;
            }
        }, 500);
    }
}
