import { Component, OnInit, OnDestroy, ElementRef, ViewChild, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ChatService } from '../../services/chat.service';
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
    channels: any[] = [];
    activeChannel: any = null;
    messages: any[] = [];
    newMessage = '';
    currentUser: string | null = '';
    currentUpn: string | null = '';

    // Presence State
    onlineUsers = new Set<string>();

    // Search State
    searchQuery = '';
    searchResults: any[] = [];
    searchTimeout: any;

    // Call State
    isCallActive = false;
    incomingCall: { callerUpn: string, callerName: string, isVideo: boolean } | null = null;

    @ViewChild('localVideo') localVideoRef!: ElementRef<HTMLVideoElement>;
    @ViewChild('remoteVideo') remoteVideoRef!: ElementRef<HTMLVideoElement>;

    // Avatar
    currentUserAvatarUrl: string | null = null;
    userAvatarMap: Record<string, string | null> = {};
    @ViewChild('avatarFileInput') avatarFileInputRef!: ElementRef<HTMLInputElement>;

    constructor(
        public chatService: ChatService,
        private authService: AuthService,
        private webrtcService: WebrtcService,
        private router: Router,
        private cdr: ChangeDetectorRef
    ) { }

    async ngOnInit() {
        if (!this.authService.getToken()) {
            this.router.navigate(['/login']);
            return;
        }

        // Decode token to get username (simple parse for now)
        const token = this.authService.getToken();
        if (token) {
            try {
                const payload = JSON.parse(atob(token.split('.')[1]));
                this.currentUser = payload.DisplayName || payload.name || payload.unique_name;
                this.currentUpn = payload.name || payload.unique_name;
            } catch (e) { }
        }

        this.loadUserProfiles();

        await this.chatService.startConnection();

        // Assign SignalR hub to WebRTC service
        if (this.chatService.hubConnectionRef) {
            this.webrtcService.setHubConnection(this.chatService.hubConnectionRef);
        }

        // Fetch initial online users
        this.authService.getOnlineUsers().subscribe(users => {
            users.forEach(u => this.onlineUsers.add(u.toLowerCase()));
        });

        // Listen for presence events
        this.chatService.userOnline$.subscribe(upn => {
            this.onlineUsers.add(upn.toLowerCase());
            // Update search results if they are visible
            const user = this.searchResults.find(u => u.adUpn.toLowerCase() === upn.toLowerCase());
            if (user) {
                user.isOnline = true;
            }
        });

        this.chatService.userOffline$.subscribe(upn => {
            this.onlineUsers.delete(upn.toLowerCase());
            // Update search results
            const user = this.searchResults.find(u => u.adUpn.toLowerCase() === upn.toLowerCase());
            if (user) {
                user.isOnline = false;
                user.lastSeen = new Date().toISOString();
            }
        });

        // Subscribe to WebRTC events
        this.webrtcService.onIncomingCall.subscribe(data => {
            this.incomingCall = data;
        });

        this.webrtcService.onRemoteStreamAdd.subscribe(stream => {
            if (this.remoteVideoRef && this.remoteVideoRef.nativeElement) {
                this.remoteVideoRef.nativeElement.srcObject = stream;
            }
        });

        this.webrtcService.onCallEnded.subscribe(() => {
            this.isCallActive = false;
            this.incomingCall = null;
        });

        this.chatService.getChannels().subscribe(chans => {
            this.channels = chans;
            if (this.channels.length > 0) {
                this.selectChannel(this.channels[0]);
            }
        });

        this.chatService.messages$.subscribe(msgs => {
            this.messages = msgs;
            this.scrollToBottom();
        });
    }

    async selectChannel(channel: any) {
        if (this.activeChannel) {
            this.chatService.leaveChannel(this.activeChannel.id);
        }
        this.activeChannel = channel;
        this.chatService.joinChannel(channel.id);
        this.chatService.getChannelMessages(channel.id);
    }

    formatChannelName(name: string): string {
        if (!name) return '';
        if (!name.startsWith('DM_')) return name;

        const parts = name.split('_');
        if (parts.length >= 3) {
            const upn1 = parts[1];
            const upn2 = parts[2];

            // Find the other user's UPN
            let targetUpn = upn1;
            if (this.currentUpn && upn1.toLowerCase() === this.currentUpn.toLowerCase()) {
                targetUpn = upn2;
            } else if (this.currentUpn && upn2.toLowerCase() === this.currentUpn.toLowerCase()) {
                targetUpn = upn1;
            }

            // Clean up the UPN to look like a Display Name (e.g. nisha.kurian@... -> Nisha Kurian)
            const namePart = targetUpn.split('@')[0];
            return namePart.split('.').map(s => s.charAt(0).toUpperCase() + s.slice(1)).join(' ');
        }
        return name;
    }

    getDmTargetUpn(name: string): string | null {
        if (!name || !name.startsWith('DM_')) return null;
        const parts = name.split('_');
        if (parts.length >= 3) {
            const upn1 = parts[1];
            const upn2 = parts[2];
            if (this.currentUpn && upn1.toLowerCase() === this.currentUpn.toLowerCase()) return upn2;
            return upn1;
        }
        return null;
    }

    isDmOnline(channelName: string): boolean {
        const targetUpn = this.getDmTargetUpn(channelName);
        if (!targetUpn) return false;
        return this.onlineUsers.has(targetUpn.toLowerCase());
    }

    onSearchChange() {
        if (this.searchTimeout) clearTimeout(this.searchTimeout);

        if (!this.searchQuery || this.searchQuery.trim().length === 0) {
            this.searchResults = [];
            return;
        }

        this.searchTimeout = setTimeout(() => {
            this.authService.searchUsers(this.searchQuery).subscribe(users => {
                this.searchResults = users;
            });
        }, 500); // debounce 500ms
    }

    startDm(targetUser: any) {
        this.searchQuery = '';
        this.searchResults = [];
        this.chatService.getOrCreateDm(targetUser.adUpn, targetUser.displayName).subscribe(dmChannel => {
            // Check if it's already in the list
            const exists = this.channels.find(c => c.id === dmChannel.id);
            if (!exists) {
                this.channels.unshift(dmChannel);
            }
            this.selectChannel(dmChannel);
        });
    }

    async sendMessage() {
        if (!this.newMessage.trim() || !this.activeChannel) return;
        await this.chatService.sendMessage(this.activeChannel.id, this.newMessage);
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
        if (this.activeChannel) {
            this.chatService.leaveChannel(this.activeChannel.id);
        }
    }

    private scrollToBottom() {
        setTimeout(() => {
            const container = document.querySelector('.chat-history');
            if (container) {
                container.scrollTop = container.scrollHeight;
            }
        }, 50);
    }

    // --- Calling Methods --- //

    startCall(isVideo: boolean) {
        if (!this.activeChannel || !this.activeChannel.name.startsWith('DM_')) {
            alert('Calling is only supported in Direct Messages for now.');
            return;
        }

        const targetUpn = this.getDmTargetUpn(this.activeChannel.name);
        if (!targetUpn) {
            console.error("Could not determine target UPN for call.");
            return;
        }

        this.webrtcService.initiateCall(targetUpn, isVideo);
        this.isCallActive = true;
        this.attachLocalStream();
    }

    acceptCall() {
        if (!this.incomingCall) return;
        this.webrtcService.answerCall(this.incomingCall.callerUpn, this.incomingCall.isVideo);
        this.isCallActive = true;

        const callData = this.incomingCall;
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
            if (this.localVideoRef && this.localVideoRef.nativeElement && this.webrtcService.localStream) {
                this.localVideoRef.nativeElement.srcObject = this.webrtcService.localStream;
            }
        }, 500);
    }
}
