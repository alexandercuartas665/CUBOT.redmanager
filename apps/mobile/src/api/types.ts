// Tipos espejo del backend (Application/Mobile/IMobileService.cs).
// Cambia el backend → cambia esto → cambian los consumers. TypeScript nos avisa en compile time.

export interface MobileUser {
  id: string;
  email: string;
  displayName: string;
}

export interface MobileTenant {
  id: string;
  name: string;
}

export interface MobileLoginRequest {
  email: string;
  password: string;
  tenantId?: string | null;
  deviceLabel?: string | null;
}

export interface MobileLoginResponse {
  apiToken: string | null;
  expiresAt: string | null;
  user: MobileUser | null;
  tenant: MobileTenant | null;
  tenantSelectionRequired: boolean;
  availableTenants: MobileTenant[];
}

export interface MobileDashboard {
  conversationsActive: number;
  messagesLast7Days: number;
  inboundLast7Days: number;
  outboundLast7Days: number;
  agentsConfigured: number;
  agentsWithFuxion: number;
  tokensExpiringSoon: number;
  videosSynced: number;
  pendingComments: number;
  lastTikTokSyncAt: string | null;
}

export interface MobileConversation {
  id: string;
  contactName: string;
  contactPhone: string;
  lineLabel: string | null;
  lastMessageAt: string | null;
  lastMessagePreview: string | null;
  lastMessageDirection: string;
}

export interface MobileMessage {
  id: string;
  direction: 'inbound' | 'outbound' | '';
  body: string | null;
  sentAt: string;
  mediaType: string | null;
  mediaUrl: string | null;
  sentByName: string | null;
}

export interface MobileAgent {
  id: string;
  name: string;
  role: string | null;
  isActive: boolean;
  paymentEnabled: boolean;
  paymentTokenPresent: boolean;
  paymentTokenExpiresAt: string | null;
  paymentLastPriceSyncAt: string | null;
}

export interface MobileSyncPricesResult {
  ok: boolean;
  rowsChecked: number;
  rowsUpdated: number;
  rowsAlreadyOk: number;
  rowsSkipped: number;
  errors: string[];
  syncedAt: string | null;
}
