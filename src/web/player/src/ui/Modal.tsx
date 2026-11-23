import { useEffect, type ReactNode } from 'react';

interface ModalProps {
  open?: boolean;
  isOpen?: boolean;
  title: string;
  onClose: () => void;
  children: ReactNode;
}

export function Modal({ open, isOpen, title, onClose, children }: ModalProps) {
  const isVisible = open ?? isOpen ?? false;
  useEffect(() => {
    if (!isVisible) return;
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose();
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [isVisible, onClose]);

  if (!isVisible) return null;

  return (
    <div className="modal-backdrop" onClick={onClose}>
      <div className="modal-dialog" onClick={(e) => e.stopPropagation()}>
        <div className="modal-header">
          <h3 className="modal-title">{title}</h3>
          <button type="button" className="modal-close-btn" onClick={onClose} aria-label="Close">
            ✕
          </button>
        </div>
        <div className="modal-body">{children}</div>
      </div>
    </div>
  );
}
