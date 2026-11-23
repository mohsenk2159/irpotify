import type { CSSProperties } from 'react';

export type ThreeDIconType =
  | 'vinyl'
  | 'headphones'
  | 'notes'
  | 'album'
  | 'mic'
  | 'folder'
  | 'cassette'
  | 'heart'
  | 'cloud'
  | 'users'
  | 'shield'
  | 'telegram'
  | 'disk'
  | 'rocket'
  | 'waves';

interface ThreeDIconProps {
  type: ThreeDIconType;
  size?: number;
  className?: string;
  style?: CSSProperties;
  animated?: boolean;
}

export function ThreeDIcon({ type, size = 56, className = '', style, animated = false }: ThreeDIconProps) {
  const containerStyle: CSSProperties = {
    width: size,
    height: size,
    display: 'inline-flex',
    alignItems: 'center',
    justifyContent: 'center',
    filter: 'drop-shadow(0 8px 16px rgba(0,0,0,0.35))',
    transition: 'transform 0.3s cubic-bezier(0.34, 1.56, 0.64, 1)',
    ...style,
  };

  switch (type) {
    case 'vinyl':
      return <ThreeDVinyl size={size} animated={animated} containerStyle={containerStyle} className={className} />;
    case 'headphones':
      return <ThreeDHeadphones size={size} containerStyle={containerStyle} className={className} />;
    case 'notes':
      return <ThreeDNotes size={size} containerStyle={containerStyle} className={className} />;
    case 'album':
      return <ThreeDAlbum size={size} containerStyle={containerStyle} className={className} />;
    case 'mic':
      return <ThreeDMic size={size} containerStyle={containerStyle} className={className} />;
    case 'folder':
      return <ThreeDFolder size={size} containerStyle={containerStyle} className={className} />;
    case 'cassette':
      return <ThreeDCassette size={size} containerStyle={containerStyle} className={className} />;
    case 'heart':
      return <ThreeDHeart size={size} animated={animated} containerStyle={containerStyle} className={className} />;
    case 'cloud':
      return <ThreeDCloud size={size} containerStyle={containerStyle} className={className} />;
    case 'users':
      return <ThreeDUsers size={size} containerStyle={containerStyle} className={className} />;
    case 'shield':
      return <ThreeDShield size={size} containerStyle={containerStyle} className={className} />;
    case 'telegram':
      return <ThreeDTelegram size={size} containerStyle={containerStyle} className={className} />;
    case 'disk':
      return <ThreeDDisk size={size} containerStyle={containerStyle} className={className} />;
    case 'rocket':
      return <ThreeDRocket size={size} containerStyle={containerStyle} className={className} />;
    case 'waves':
      return <ThreeDWaves size={size} animated={animated} containerStyle={containerStyle} className={className} />;
    default:
      return <ThreeDVinyl size={size} animated={animated} containerStyle={containerStyle} className={className} />;
  }
}

// 1. 3D Vinyl Record
function ThreeDVinyl({ size, animated, containerStyle, className }: { size: number; animated: boolean; containerStyle: CSSProperties; className: string }) {
  return (
    <div className={`threed-icon threed-vinyl ${animated ? 'threed-spin' : ''} ${className}`} style={containerStyle}>
      <svg viewBox="0 0 120 120" width={size} height={size} fill="none" xmlns="http://www.w3.org/2000/svg">
        <defs>
          <radialGradient id="v-body" cx="40%" cy="35%" r="65%">
            <stop offset="0%" stopColor="#2a2a34" />
            <stop offset="45%" stopColor="#15151c" />
            <stop offset="85%" stopColor="#0a0a0f" />
            <stop offset="100%" stopColor="#000000" />
          </radialGradient>
          <linearGradient id="v-sheen-1" x1="0%" y1="0%" x2="100%" y2="100%">
            <stop offset="0%" stopColor="rgba(255,255,255,0.22)" />
            <stop offset="35%" stopColor="rgba(255,255,255,0.02)" />
            <stop offset="50%" stopColor="transparent" />
            <stop offset="65%" stopColor="rgba(255,255,255,0.02)" />
            <stop offset="100%" stopColor="rgba(255,255,255,0.25)" />
          </linearGradient>
          <linearGradient id="v-label" x1="0%" y1="0%" x2="100%" y2="100%">
            <stop offset="0%" stopColor="#1ed760" />
            <stop offset="50%" stopColor="#00c49f" />
            <stop offset="100%" stopColor="#1db954" />
          </linearGradient>
          <linearGradient id="v-rim" x1="20%" y1="0%" x2="80%" y2="100%">
            <stop offset="0%" stopColor="#555" />
            <stop offset="50%" stopColor="#111" />
            <stop offset="100%" stopColor="#333" />
          </linearGradient>
        </defs>

        {/* 3D Drop Shadow Disc */}
        <circle cx="60" cy="64" r="54" fill="rgba(0,0,0,0.5)" filter="blur(6px)" />

        {/* Vinyl Base */}
        <circle cx="60" cy="60" r="54" fill="url(#v-body)" stroke="url(#v-rim)" strokeWidth="2" />

        {/* Realistic Micro-Grooves */}
        <circle cx="60" cy="60" r="48" stroke="rgba(255,255,255,0.06)" strokeWidth="1.2" />
        <circle cx="60" cy="60" r="44" stroke="rgba(0,0,0,0.6)" strokeWidth="0.8" />
        <circle cx="60" cy="60" r="41" stroke="rgba(255,255,255,0.04)" strokeWidth="1" />
        <circle cx="60" cy="60" r="37" stroke="rgba(0,0,0,0.5)" strokeWidth="0.8" />
        <circle cx="60" cy="60" r="34" stroke="rgba(255,255,255,0.05)" strokeWidth="0.9" />
        <circle cx="60" cy="60" r="30" stroke="rgba(0,0,0,0.4)" strokeWidth="0.7" />

        {/* Specular Light Reflection Butterfly */}
        <path d="M60 60 L18 20 A54 54 0 0 1 102 20 Z" fill="url(#v-sheen-1)" opacity="0.65" />
        <path d="M60 60 L18 100 A54 54 0 0 0 102 100 Z" fill="url(#v-sheen-1)" opacity="0.65" />

        {/* Center Label */}
        <circle cx="60" cy="60" r="21" fill="url(#v-label)" stroke="#111" strokeWidth="1.5" />
        <circle cx="60" cy="60" r="16" stroke="rgba(255,255,255,0.25)" strokeWidth="0.8" fill="none" />

        {/* Center Spindle Hole with 3D Depth */}
        <circle cx="60" cy="60" r="6" fill="#0c0d12" stroke="#222" strokeWidth="1" />
        <circle cx="60" cy="60" r="4" fill="#000" />
        <circle cx="58.5" cy="58.5" r="1.5" fill="rgba(255,255,255,0.6)" />
      </svg>
    </div>
  );
}

// 2. 3D Studio Headphones
function ThreeDHeadphones({ size, containerStyle, className }: { size: number; containerStyle: CSSProperties; className: string }) {
  return (
    <div className={`threed-icon threed-headphones ${className}`} style={containerStyle}>
      <svg viewBox="0 0 120 120" width={size} height={size} fill="none" xmlns="http://www.w3.org/2000/svg">
        <defs>
          <linearGradient id="hp-band" x1="0%" y1="0%" x2="100%" y2="100%">
            <stop offset="0%" stopColor="#4a4f5f" />
            <stop offset="50%" stopColor="#1e2230" />
            <stop offset="100%" stopColor="#0d0f17" />
          </linearGradient>
          <linearGradient id="hp-cushion" x1="0%" y1="0%" x2="100%" y2="100%">
            <stop offset="0%" stopColor="#1ed760" />
            <stop offset="100%" stopColor="#0e8f3d" />
          </linearGradient>
          <linearGradient id="hp-spec" x1="0%" y1="0%" x2="0%" y2="100%">
            <stop offset="0%" stopColor="rgba(255,255,255,0.45)" />
            <stop offset="100%" stopColor="transparent" />
          </linearGradient>
        </defs>

        {/* Headband Arch */}
        <path
          d="M26 68 C22 24, 98 24, 94 68"
          stroke="url(#hp-band)"
          strokeWidth="11"
          strokeLinecap="round"
        />
        {/* Headband Highlight */}
        <path
          d="M34 50 C36 28, 84 28, 86 50"
          stroke="url(#hp-spec)"
          strokeWidth="3"
          strokeLinecap="round"
        />

        {/* Left Ear Cup */}
        <g transform="translate(14, 60)">
          <rect x="0" y="0" width="18" height="34" rx="9" fill="#13151f" stroke="#2c3040" strokeWidth="2" />
          <rect x="3" y="4" width="12" height="26" rx="6" fill="url(#hp-cushion)" />
          <rect x="5" y="6" width="3" height="12" rx="1.5" fill="rgba(255,255,255,0.5)" />
        </g>

        {/* Right Ear Cup */}
        <g transform="translate(88, 60)">
          <rect x="0" y="0" width="18" height="34" rx="9" fill="#13151f" stroke="#2c3040" strokeWidth="2" />
          <rect x="3" y="4" width="12" height="26" rx="6" fill="url(#hp-cushion)" />
          <rect x="5" y="6" width="3" height="12" rx="1.5" fill="rgba(255,255,255,0.5)" />
        </g>

        {/* Sound Sparkles */}
        <circle cx="60" cy="58" r="3" fill="#1ed760" filter="drop-shadow(0 0 6px #1ed760)" />
      </svg>
    </div>
  );
}

// 3. 3D Musical Notes
function ThreeDNotes({ size, containerStyle, className }: { size: number; containerStyle: CSSProperties; className: string }) {
  return (
    <div className={`threed-icon threed-notes ${className}`} style={containerStyle}>
      <svg viewBox="0 0 120 120" width={size} height={size} fill="none" xmlns="http://www.w3.org/2000/svg">
        <defs>
          <linearGradient id="note-grad" x1="0%" y1="0%" x2="100%" y2="100%">
            <stop offset="0%" stopColor="#38bdf8" />
            <stop offset="50%" stopColor="#6366f1" />
            <stop offset="100%" stopColor="#a855f7" />
          </linearGradient>
          <linearGradient id="note-gold" x1="0%" y1="0%" x2="100%" y2="100%">
            <stop offset="0%" stopColor="#1ed760" />
            <stop offset="100%" stopColor="#059669" />
          </linearGradient>
        </defs>

        {/* Big Double Note */}
        <g transform="translate(18, 16)">
          <ellipse cx="26" cy="74" rx="16" ry="12" transform="rotate(-25 26 74)" fill="url(#note-grad)" />
          <ellipse cx="70" cy="62" rx="16" ry="12" transform="rotate(-25 70 62)" fill="url(#note-grad)" />
          {/* Stems */}
          <rect x="33" y="16" width="8" height="58" rx="3" fill="url(#note-grad)" />
          <rect x="77" y="6" width="8" height="56" rx="3" fill="url(#note-grad)" />
          {/* Top Beam with 3D Depth */}
          <path d="M33 16 L85 6 L85 20 L33 30 Z" fill="url(#note-gold)" />
          {/* Highlights */}
          <ellipse cx="24" cy="72" rx="7" ry="4" transform="rotate(-25 24 72)" fill="rgba(255,255,255,0.4)" />
          <ellipse cx="68" cy="60" rx="7" ry="4" transform="rotate(-25 68 60)" fill="rgba(255,255,255,0.4)" />
        </g>
      </svg>
    </div>
  );
}

// 4. 3D Album Box with Sliding Disc
function ThreeDAlbum({ size, containerStyle, className }: { size: number; containerStyle: CSSProperties; className: string }) {
  return (
    <div className={`threed-icon threed-album ${className}`} style={containerStyle}>
      <svg viewBox="0 0 120 120" width={size} height={size} fill="none" xmlns="http://www.w3.org/2000/svg">
        <defs>
          <linearGradient id="album-sleeve" x1="0%" y1="0%" x2="100%" y2="100%">
            <stop offset="0%" stopColor="#252836" />
            <stop offset="50%" stopColor="#181a24" />
            <stop offset="100%" stopColor="#0f1118" />
          </linearGradient>
          <linearGradient id="album-accent" x1="0%" y1="0%" x2="100%" y2="100%">
            <stop offset="0%" stopColor="#f43f5e" />
            <stop offset="100%" stopColor="#e11d48" />
          </linearGradient>
        </defs>

        {/* Sliding Vinyl Disc Behind Sleeve */}
        <g transform="translate(36, 12)">
          <circle cx="46" cy="46" r="36" fill="#121217" stroke="#222" strokeWidth="1" />
          <circle cx="46" cy="46" r="28" stroke="rgba(255,255,255,0.06)" strokeWidth="0.8" />
          <circle cx="46" cy="46" r="14" fill="url(#album-accent)" />
          <circle cx="46" cy="46" r="4" fill="#000" />
        </g>

        {/* Outer Album Sleeve in 3D Perspective */}
        <rect x="14" y="22" width="76" height="76" rx="8" fill="url(#album-sleeve)" stroke="#383c50" strokeWidth="2" />
        {/* Album Artwork Label on Sleeve */}
        <rect x="22" y="30" width="60" height="60" rx="6" fill="#1e2230" />
        <circle cx="52" cy="60" r="18" fill="url(#album-accent)" opacity="0.85" />
        <path d="M48 52 L60 60 L48 68 Z" fill="#ffffff" />
        {/* Spine Light Reflection */}
        <line x1="16" y1="24" x2="16" y2="96" stroke="rgba(255,255,255,0.3)" strokeWidth="2" strokeLinecap="round" />
      </svg>
    </div>
  );
}

// 5. 3D Studio Vintage Microphone
function ThreeDMic({ size, containerStyle, className }: { size: number; containerStyle: CSSProperties; className: string }) {
  return (
    <div className={`threed-icon threed-mic ${className}`} style={containerStyle}>
      <svg viewBox="0 0 120 120" width={size} height={size} fill="none" xmlns="http://www.w3.org/2000/svg">
        <defs>
          <linearGradient id="mic-body" x1="0%" y1="0%" x2="100%" y2="100%">
            <stop offset="0%" stopColor="#64748b" />
            <stop offset="50%" stopColor="#334155" />
            <stop offset="100%" stopColor="#0f172a" />
          </linearGradient>
          <linearGradient id="mic-neon" x1="0%" y1="0%" x2="100%" y2="100%">
            <stop offset="0%" stopColor="#a855f7" />
            <stop offset="100%" stopColor="#6366f1" />
          </linearGradient>
        </defs>

        {/* Mic Capsule */}
        <rect x="42" y="16" width="36" height="54" rx="18" fill="url(#mic-body)" stroke="#94a3b8" strokeWidth="2" />
        {/* Metallic Grille Lines */}
        <line x1="43" y1="32" x2="77" y2="32" stroke="rgba(255,255,255,0.3)" strokeWidth="1.5" />
        <line x1="43" y1="44" x2="77" y2="44" stroke="rgba(255,255,255,0.3)" strokeWidth="1.5" />
        <line x1="60" y1="18" x2="60" y2="58" stroke="rgba(255,255,255,0.25)" strokeWidth="1.5" />

        {/* Glowing Indicator Ring */}
        <rect x="42" y="58" width="36" height="6" fill="url(#mic-neon)" />

        {/* Mic U-Mount Frame */}
        <path d="M30 46 C30 82, 90 82, 90 46" stroke="#475569" strokeWidth="5" strokeLinecap="round" fill="none" />
        {/* Neck and Round Base */}
        <line x1="60" y1="80" x2="60" y2="98" stroke="#334155" strokeWidth="6" strokeLinecap="round" />
        <ellipse cx="60" cy="100" rx="24" ry="7" fill="#1e293b" stroke="#475569" strokeWidth="2" />
      </svg>
    </div>
  );
}

// 6. 3D Isometric Glowing Folder
function ThreeDFolder({ size, containerStyle, className }: { size: number; containerStyle: CSSProperties; className: string }) {
  return (
    <div className={`threed-icon threed-folder ${className}`} style={containerStyle}>
      <svg viewBox="0 0 120 120" width={size} height={size} fill="none" xmlns="http://www.w3.org/2000/svg">
        <defs>
          <linearGradient id="f-back" x1="0%" y1="0%" x2="100%" y2="100%">
            <stop offset="0%" stopColor="#f59e0b" />
            <stop offset="100%" stopColor="#d97706" />
          </linearGradient>
          <linearGradient id="f-front" x1="0%" y1="0%" x2="100%" y2="100%">
            <stop offset="0%" stopColor="#fbbf24" />
            <stop offset="100%" stopColor="#f59e0b" />
          </linearGradient>
        </defs>

        {/* Back Tab */}
        <path d="M18 36 C18 31, 22 28, 27 28 L50 28 L60 38 L93 38 C98 38, 102 42, 102 47 L102 85 C102 90, 98 94, 93 94 L27 94 C22 94, 18 90, 18 85 Z" fill="url(#f-back)" />

        {/* Sheet Inside */}
        <rect x="28" y="34" width="64" height="46" rx="5" fill="#ffffff" opacity="0.9" />
        <line x1="38" y1="46" x2="68" y2="46" stroke="#cbd5e1" strokeWidth="3" strokeLinecap="round" />
        <line x1="38" y1="56" x2="80" y2="56" stroke="#cbd5e1" strokeWidth="3" strokeLinecap="round" />

        {/* Front Isometric Flap */}
        <path d="M14 50 L106 50 L98 94 C98 98, 94 100, 90 100 L24 100 C20 100, 16 98, 14 94 Z" fill="url(#f-front)" />
        {/* Specular Highlight on flap */}
        <line x1="18" y1="53" x2="102" y2="53" stroke="rgba(255,255,255,0.6)" strokeWidth="2" strokeLinecap="round" />
      </svg>
    </div>
  );
}

// 7. 3D Retro Cassette Tape (Playlists)
function ThreeDCassette({ size, containerStyle, className }: { size: number; containerStyle: CSSProperties; className: string }) {
  return (
    <div className={`threed-icon threed-cassette ${className}`} style={containerStyle}>
      <svg viewBox="0 0 120 120" width={size} height={size} fill="none" xmlns="http://www.w3.org/2000/svg">
        <defs>
          <linearGradient id="cas-body" x1="0%" y1="0%" x2="100%" y2="100%">
            <stop offset="0%" stopColor="#312e81" />
            <stop offset="50%" stopColor="#1e1b4b" />
            <stop offset="100%" stopColor="#0f0e26" />
          </linearGradient>
          <linearGradient id="cas-label" x1="0%" y1="0%" x2="100%" y2="100%">
            <stop offset="0%" stopColor="#ec4899" />
            <stop offset="100%" stopColor="#8b5cf6" />
          </linearGradient>
        </defs>

        {/* Shell */}
        <rect x="14" y="28" width="92" height="64" rx="8" fill="url(#cas-body)" stroke="#4338ca" strokeWidth="2" />
        {/* Center Label */}
        <rect x="24" y="36" width="72" height="38" rx="4" fill="url(#cas-label)" />
        {/* Spool Window */}
        <rect x="36" y="44" width="48" height="22" rx="11" fill="#0f0e26" stroke="#ffffff" strokeWidth="1.2" />
        {/* Left Spool Gear */}
        <circle cx="48" cy="55" r="7" fill="#ffffff" />
        <circle cx="48" cy="55" r="3" fill="#0f0e26" />
        {/* Right Spool Gear */}
        <circle cx="72" cy="55" r="7" fill="#ffffff" />
        <circle cx="72" cy="55" r="3" fill="#0f0e26" />
        {/* Bottom Trapezoid Notch */}
        <polygon points="34,92 42,78 78,78 86,92" fill="#18182b" stroke="#3730a3" strokeWidth="1" />
      </svg>
    </div>
  );
}

// 8. 3D Puffy Glossy Heart
function ThreeDHeart({ size, animated, containerStyle, className }: { size: number; animated: boolean; containerStyle: CSSProperties; className: string }) {
  return (
    <div className={`threed-icon threed-heart ${animated ? 'threed-pulse' : ''} ${className}`} style={containerStyle}>
      <svg viewBox="0 0 120 120" width={size} height={size} fill="none" xmlns="http://www.w3.org/2000/svg">
        <defs>
          <radialGradient id="h-grad" cx="35%" cy="30%" r="65%">
            <stop offset="0%" stopColor="#ff4d79" />
            <stop offset="50%" stopColor="#e11d48" />
            <stop offset="90%" stopColor="#9f1239" />
            <stop offset="100%" stopColor="#4c0519" />
          </radialGradient>
        </defs>

        <path
          d="M60 98 C35 78, 16 58, 16 38 C16 22, 28 14, 42 14 C50 14, 56 18, 60 25 C64 18, 70 14, 78 14 C92 14, 104 22, 104 38 C104 58, 85 78, 60 98 Z"
          fill="url(#h-grad)"
          filter="drop-shadow(0 6px 12px rgba(225,29,72,0.45))"
        />
        {/* Specular 3D Highlight Bubble */}
        <ellipse cx="38" cy="28" rx="10" ry="6" transform="rotate(-30 38 28)" fill="rgba(255,255,255,0.6)" />
        <circle cx="48" cy="22" r="2.5" fill="rgba(255,255,255,0.8)" />
      </svg>
    </div>
  );
}

// 9. 3D Cloud Upload
function ThreeDCloud({ size, containerStyle, className }: { size: number; containerStyle: CSSProperties; className: string }) {
  return (
    <div className={`threed-icon threed-cloud ${className}`} style={containerStyle}>
      <svg viewBox="0 0 120 120" width={size} height={size} fill="none" xmlns="http://www.w3.org/2000/svg">
        <defs>
          <linearGradient id="cloud-grad" x1="0%" y1="0%" x2="100%" y2="100%">
            <stop offset="0%" stopColor="#38bdf8" />
            <stop offset="100%" stopColor="#0284c7" />
          </linearGradient>
        </defs>

        {/* Puffy Cloud Body */}
        <path
          d="M34 82 C20 82, 14 70, 20 58 C20 44, 34 38, 44 42 C50 26, 72 24, 82 34 C94 32, 104 42, 102 54 C110 60, 108 82, 92 82 Z"
          fill="url(#cloud-grad)"
        />
        {/* Cloud Top Shine */}
        <path d="M46 42 C54 28, 72 26, 80 34" stroke="rgba(255,255,255,0.6)" strokeWidth="3" strokeLinecap="round" />

        {/* 3D Arrow Up */}
        <g transform="translate(60, 62)">
          <path d="M0 -18 L12 -6 L4 -6 L4 14 L-4 14 L-4 -6 L-12 -6 Z" fill="#ffffff" filter="drop-shadow(0 2px 4px rgba(0,0,0,0.3))" />
        </g>
      </svg>
    </div>
  );
}

// 10. 3D Users / Community
function ThreeDUsers({ size, containerStyle, className }: { size: number; containerStyle: CSSProperties; className: string }) {
  return (
    <div className={`threed-icon threed-users ${className}`} style={containerStyle}>
      <svg viewBox="0 0 120 120" width={size} height={size} fill="none" xmlns="http://www.w3.org/2000/svg">
        <defs>
          <linearGradient id="u-main" x1="0%" y1="0%" x2="100%" y2="100%">
            <stop offset="0%" stopColor="#10b981" />
            <stop offset="100%" stopColor="#047857" />
          </linearGradient>
          <linearGradient id="u-sub" x1="0%" y1="0%" x2="100%" y2="100%">
            <stop offset="0%" stopColor="#06b6d4" />
            <stop offset="100%" stopColor="#0e7490" />
          </linearGradient>
        </defs>

        {/* Secondary user (back) */}
        <g transform="translate(24, 0)" opacity="0.75">
          <circle cx="58" cy="38" r="14" fill="url(#u-sub)" />
          <path d="M38 84 C38 68, 78 68, 78 84" stroke="url(#u-sub)" strokeWidth="12" strokeLinecap="round" />
        </g>

        {/* Primary user (front) */}
        <circle cx="48" cy="44" r="18" fill="url(#u-main)" stroke="#ffffff" strokeWidth="2" />
        <ellipse cx="44" cy="38" rx="6" ry="3" fill="rgba(255,255,255,0.45)" />
        <path d="M22 96 C22 76, 74 76, 74 96" stroke="url(#u-main)" strokeWidth="16" strokeLinecap="round" />
      </svg>
    </div>
  );
}

// 11. 3D Cyber Shield
function ThreeDShield({ size, containerStyle, className }: { size: number; containerStyle: CSSProperties; className: string }) {
  return (
    <div className={`threed-icon threed-shield ${className}`} style={containerStyle}>
      <svg viewBox="0 0 120 120" width={size} height={size} fill="none" xmlns="http://www.w3.org/2000/svg">
        <defs>
          <linearGradient id="sh-body" x1="0%" y1="0%" x2="100%" y2="100%">
            <stop offset="0%" stopColor="#10b981" />
            <stop offset="50%" stopColor="#059669" />
            <stop offset="100%" stopColor="#064e3b" />
          </linearGradient>
        </defs>

        <path
          d="M60 16 L22 32 C22 70, 48 94, 60 104 C72 94, 98 70, 98 32 Z"
          fill="url(#sh-body)"
          stroke="#34d399"
          strokeWidth="3"
        />
        {/* Center Checkmark / Lock Emblem */}
        <circle cx="60" cy="54" r="14" fill="#ffffff" opacity="0.9" />
        <circle cx="60" cy="54" r="6" fill="#047857" />
        <rect x="57" y="54" width="6" height="12" rx="2" fill="#047857" />
      </svg>
    </div>
  );
}

// 12. 3D Telegram Paper Plane
function ThreeDTelegram({ size, containerStyle, className }: { size: number; containerStyle: CSSProperties; className: string }) {
  return (
    <div className={`threed-icon threed-telegram ${className}`} style={containerStyle}>
      <svg viewBox="0 0 120 120" width={size} height={size} fill="none" xmlns="http://www.w3.org/2000/svg">
        <defs>
          <linearGradient id="tg-disc" x1="0%" y1="0%" x2="100%" y2="100%">
            <stop offset="0%" stopColor="#2aabee" />
            <stop offset="100%" stopColor="#229ed9" />
          </linearGradient>
        </defs>

        <circle cx="60" cy="60" r="50" fill="url(#tg-disc)" />
        <path
          d="M92 34 L26 59 C24 60, 24 63, 27 64 L44 69 L51 90 C52 92, 54 92, 55 90 L64 81 L79 92 C82 94, 85 92, 86 89 L96 38 C97 34, 94 32, 92 34 Z"
          fill="#ffffff"
        />
        <path d="M44 69 L78 46 L51 77 Z" fill="#d2eaf8" />
      </svg>
    </div>
  );
}

// 13. 3D Storage Disk
function ThreeDDisk({ size, containerStyle, className }: { size: number; containerStyle: CSSProperties; className: string }) {
  return (
    <div className={`threed-icon threed-disk ${className}`} style={containerStyle}>
      <svg viewBox="0 0 120 120" width={size} height={size} fill="none" xmlns="http://www.w3.org/2000/svg">
        <defs>
          <linearGradient id="disk-metal" x1="0%" y1="0%" x2="100%" y2="100%">
            <stop offset="0%" stopColor="#475569" />
            <stop offset="50%" stopColor="#1e293b" />
            <stop offset="100%" stopColor="#0f172a" />
          </linearGradient>
        </defs>

        {/* Stack of Platter Plates */}
        <ellipse cx="60" cy="84" rx="42" ry="16" fill="url(#disk-metal)" stroke="#64748b" strokeWidth="2" />
        <ellipse cx="60" cy="64" rx="42" ry="16" fill="url(#disk-metal)" stroke="#64748b" strokeWidth="2" />
        <ellipse cx="60" cy="44" rx="42" ry="16" fill="url(#disk-metal)" stroke="#94a3b8" strokeWidth="2" />

        {/* Status LEDs */}
        <circle cx="86" cy="44" r="3" fill="#10b981" filter="drop-shadow(0 0 4px #10b981)" />
        <circle cx="76" cy="44" r="3" fill="#38bdf8" filter="drop-shadow(0 0 4px #38bdf8)" />
      </svg>
    </div>
  );
}

// 14. 3D Speed Rocket
function ThreeDRocket({ size, containerStyle, className }: { size: number; containerStyle: CSSProperties; className: string }) {
  return (
    <div className={`threed-icon threed-rocket ${className}`} style={containerStyle}>
      <svg viewBox="0 0 120 120" width={size} height={size} fill="none" xmlns="http://www.w3.org/2000/svg">
        <defs>
          <linearGradient id="r-hull" x1="0%" y1="0%" x2="100%" y2="100%">
            <stop offset="0%" stopColor="#f8fafc" />
            <stop offset="50%" stopColor="#e2e8f0" />
            <stop offset="100%" stopColor="#94a3b8" />
          </linearGradient>
          <linearGradient id="r-fire" x1="0%" y1="0%" x2="100%" y2="100%">
            <stop offset="0%" stopColor="#f59e0b" />
            <stop offset="100%" stopColor="#ef4444" />
          </linearGradient>
        </defs>

        {/* Rocket Hull */}
        <path d="M88 24 C56 20, 36 46, 36 68 L52 84 C74 84, 100 64, 96 32 Z" fill="url(#r-hull)" />
        {/* Cockpit Window */}
        <circle cx="68" cy="46" r="9" fill="#0284c7" stroke="#ffffff" strokeWidth="2" />
        {/* Fins */}
        <polygon points="34,68 18,80 38,84" fill="#ef4444" />
        <polygon points="52,84 36,102 48,102" fill="#ef4444" />
        {/* Flame Exhaust */}
        <polygon points="36,84 14,106 32,96" fill="url(#r-fire)" filter="drop-shadow(0 0 8px #f59e0b)" />
      </svg>
    </div>
  );
}

// 15. 3D Equalizer Waves
function ThreeDWaves({ size, animated, containerStyle, className }: { size: number; animated: boolean; containerStyle: CSSProperties; className: string }) {
  return (
    <div className={`threed-icon threed-waves ${animated ? 'threed-wave-anim' : ''} ${className}`} style={containerStyle}>
      <svg viewBox="0 0 120 120" width={size} height={size} fill="none" xmlns="http://www.w3.org/2000/svg">
        <defs>
          <linearGradient id="bar-g1" x1="0%" y1="100%" x2="0%" y2="0%">
            <stop offset="0%" stopColor="#10b981" />
            <stop offset="100%" stopColor="#34d399" />
          </linearGradient>
          <linearGradient id="bar-g2" x1="0%" y1="100%" x2="0%" y2="0%">
            <stop offset="0%" stopColor="#06b6d4" />
            <stop offset="100%" stopColor="#38bdf8" />
          </linearGradient>
          <linearGradient id="bar-g3" x1="0%" y1="100%" x2="0%" y2="0%">
            <stop offset="0%" stopColor="#6366f1" />
            <stop offset="100%" stopColor="#818cf8" />
          </linearGradient>
        </defs>

        <rect x="22" y="52" width="12" height="46" rx="6" fill="url(#bar-g1)" className="threed-bar-1" />
        <rect x="42" y="32" width="12" height="66" rx="6" fill="url(#bar-g2)" className="threed-bar-2" />
        <rect x="62" y="22" width="12" height="76" rx="6" fill="url(#bar-g3)" className="threed-bar-3" />
        <rect x="82" y="44" width="12" height="54" rx="6" fill="url(#bar-g2)" className="threed-bar-4" />
      </svg>
    </div>
  );
}
