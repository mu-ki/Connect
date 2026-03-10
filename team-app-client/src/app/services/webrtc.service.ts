import { Injectable } from '@angular/core';
import { Subject } from 'rxjs';
import * as signalR from '@microsoft/signalr';

@Injectable({
    providedIn: 'root'
})
export class WebrtcService {
    private peerConnection: RTCPeerConnection | null = null;
    public localStream: MediaStream | null = null;
    public remoteStream: MediaStream | null = null;

    public onRemoteStreamAdd = new Subject<MediaStream>();
    public onIncomingCall = new Subject<{ callerUpn: string, callerName: string, isVideo: boolean }>();
    public onCallEnded = new Subject<void>();

    private hubConnection: signalR.HubConnection | null = null;
    private activeCallTargetUpn: string | null = null;

    constructor() { }

    public setHubConnection(hub: signalR.HubConnection) {
        this.hubConnection = hub;
        this.registerSignalREvents();
    }

    private registerSignalREvents() {
        if (!this.hubConnection) return;

        this.hubConnection.on('IncomingCall', async (data: { callerUpn: string, callerName: string, isVideo: boolean }) => {
            this.onIncomingCall.next(data);
        });

        this.hubConnection.on('ReceiveOffer', async (data: { callerUpn: string, sdp: string }) => {
            await this.handleReceiveOffer(data.callerUpn, data.sdp);
        });

        this.hubConnection.on('ReceiveAnswer', async (data: { responderUpn: string, sdp: string }) => {
            await this.handleReceiveAnswer(data.sdp);
        });

        this.hubConnection.on('ReceiveIceCandidate', async (data: { senderUpn: string, candidate: string }) => {
            await this.handleNewICECandidate(data.candidate);
        });

        this.hubConnection.on('CallDeclined', () => {
            this.endCall();
        });
    }

    public async initiateCall(targetUpn: string, isVideo: boolean) {
        this.activeCallTargetUpn = targetUpn;
        await this.startLocalStream(isVideo);
        await this.hubConnection?.invoke('InitiateCall', targetUpn, isVideo);
        this.createPeerConnection();
        await this.createOffer();
    }

    public async answerCall(callerUpn: string, isVideo: boolean) {
        this.activeCallTargetUpn = callerUpn;
        await this.startLocalStream(isVideo);
        // Peer connection will be created when we process the offer
    }

    public declineCall(callerUpn: string) {
        this.hubConnection?.invoke('DeclineCall', callerUpn);
    }

    public endCall() {
        if (this.peerConnection) {
            this.peerConnection.close();
            this.peerConnection = null;
        }
        if (this.localStream) {
            this.localStream.getTracks().forEach(track => track.stop());
            this.localStream = null;
        }
        this.remoteStream = null;
        this.activeCallTargetUpn = null;
        this.onCallEnded.next();
    }

    private async startLocalStream(video: boolean) {
        try {
            this.localStream = await navigator.mediaDevices.getUserMedia({
                video: video,
                audio: true
            });
        } catch (e) {
            console.error('Error getting user media:', e);
        }
    }

    private createPeerConnection() {
        const configuration: RTCConfiguration = {
            iceServers: [
                { urls: 'stun:stun.l.google.com:19302' } // Free stun server for testing
            ]
        };
        this.peerConnection = new RTCPeerConnection(configuration);

        this.peerConnection.onicecandidate = event => {
            if (event.candidate && this.activeCallTargetUpn) {
                this.hubConnection?.invoke('SendIceCandidate', this.activeCallTargetUpn, JSON.stringify(event.candidate));
            }
        };

        this.peerConnection.ontrack = event => {
            this.remoteStream = event.streams[0];
            this.onRemoteStreamAdd.next(this.remoteStream);
        };

        if (this.localStream) {
            this.localStream.getTracks().forEach(track => {
                this.peerConnection?.addTrack(track, this.localStream!);
            });
        }
    }

    private async createOffer() {
        if (!this.peerConnection || !this.activeCallTargetUpn) return;
        try {
            const offer = await this.peerConnection.createOffer();
            await this.peerConnection.setLocalDescription(offer);
            await this.hubConnection?.invoke('SendOffer', this.activeCallTargetUpn, JSON.stringify(offer));
        } catch (e) {
            console.error('Error creating offer:', e);
        }
    }

    private async handleReceiveOffer(callerUpn: string, sdpOffer: string) {
        this.activeCallTargetUpn = callerUpn;
        this.createPeerConnection();
        try {
            if (!this.peerConnection) return;
            const desc = new RTCSessionDescription(JSON.parse(sdpOffer));
            await this.peerConnection.setRemoteDescription(desc);

            const answer = await this.peerConnection.createAnswer();
            await this.peerConnection.setLocalDescription(answer);
            await this.hubConnection?.invoke('SendAnswer', this.activeCallTargetUpn, JSON.stringify(answer));
        } catch (e) {
            console.error('Error handling offer:', e);
        }
    }

    private async handleReceiveAnswer(sdpAnswer: string) {
        if (!this.peerConnection) return;
        try {
            const desc = new RTCSessionDescription(JSON.parse(sdpAnswer));
            await this.peerConnection.setRemoteDescription(desc);
        } catch (e) {
            console.error('Error handling answer:', e);
        }
    }

    private async handleNewICECandidate(candidateString: string) {
        if (!this.peerConnection) return;
        try {
            const candidate = new RTCIceCandidate(JSON.parse(candidateString));
            await this.peerConnection.addIceCandidate(candidate);
        } catch (e) {
            console.error('Error handling ICE candidate:', e);
        }
    }
}
