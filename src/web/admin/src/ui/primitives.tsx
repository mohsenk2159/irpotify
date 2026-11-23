import type {
  ButtonHTMLAttributes,
  InputHTMLAttributes,
  ReactNode,
  SelectHTMLAttributes,
  TextareaHTMLAttributes,
} from 'react';
import { useId } from 'react';
import { ApiError } from '../api/http';
import { useUi } from './UiProvider';
import { Icon, type IconName } from './icons';

/* ------------------------------------------------------------------ */
/* Buttons                                                             */
/* ------------------------------------------------------------------ */

type ButtonVariant = 'primary' | 'secondary' | 'ghost' | 'danger';

interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: ButtonVariant;
  size?: 'sm' | 'md';
  loading?: boolean;
  icon?: IconName;
}

export function Button({
  variant = 'secondary',
  size = 'md',
  loading = false,
  icon,
  children,
  className,
  disabled,
  type = 'button',
  ...rest
}: ButtonProps) {
  return (
    <button
      type={type}
      className={['btn', `btn-${variant}`, size === 'sm' ? 'btn-sm' : '', className ?? ''].filter(Boolean).join(' ')}
      disabled={disabled || loading}
      aria-busy={loading || undefined}
      {...rest}
    >
      {loading ? <span className="spinner spinner-sm" aria-hidden="true" /> : icon ? <Icon name={icon} size={18} /> : null}
      {children ? <span>{children}</span> : null}
    </button>
  );
}

export function IconButton({
  label,
  icon,
  variant = 'ghost',
  ...rest
}: Omit<ButtonProps, 'icon' | 'children'> & { label: string; icon: IconName }) {
  return (
    <button
      type="button"
      className={`btn btn-${variant} btn-icon`}
      aria-label={label}
      title={label}
      {...rest}
    >
      <Icon name={icon} size={18} />
    </button>
  );
}

/* ------------------------------------------------------------------ */
/* Form controls                                                       */
/* ------------------------------------------------------------------ */

interface FieldProps {
  label: string;
  htmlFor?: string;
  hint?: string;
  error?: string | null;
  required?: boolean;
  children: ReactNode;
}

export function Field({ label, htmlFor, hint, error, required, children }: FieldProps) {
  const hintId = useId();
  const errorId = useId();
  return (
    <div className={`field${error ? ' field-invalid' : ''}`}>
      <label className="field-label" htmlFor={htmlFor}>
        {label}
        {required ? (
          <span className="required" aria-hidden="true">
            *
          </span>
        ) : null}
      </label>
      {children}
      {hint ? (
        <p className="field-hint" id={hintId}>
          {hint}
        </p>
      ) : null}
      {error ? (
        <p className="field-error" id={errorId} role="alert">
          {error}
        </p>
      ) : null}
    </div>
  );
}

export function Input({ className, ...rest }: InputHTMLAttributes<HTMLInputElement>) {
  return <input className={['input', className ?? ''].filter(Boolean).join(' ')} {...rest} />;
}

export function Textarea({ className, ...rest }: TextareaHTMLAttributes<HTMLTextAreaElement>) {
  return <textarea className={['input textarea', className ?? ''].filter(Boolean).join(' ')} {...rest} />;
}

export interface SelectOption {
  value: string;
  label: string;
}

interface SelectProps extends SelectHTMLAttributes<HTMLSelectElement> {
  options?: SelectOption[];
}

export function Select({ options, className, children, ...rest }: SelectProps) {
  return (
    <select className={['input select', className ?? ''].filter(Boolean).join(' ')} {...rest}>
      {options
        ? options.map((option) => (
            <option key={option.value} value={option.value}>
              {option.label}
            </option>
          ))
        : children}
    </select>
  );
}

interface CheckboxProps extends Omit<InputHTMLAttributes<HTMLInputElement>, 'type'> {
  label: string;
  description?: string;
}

export function Checkbox({ label, description, id, ...rest }: CheckboxProps) {
  const generatedId = useId();
  const inputId = id ?? generatedId;
  const describeId = useId();
  return (
    <div className="checkbox-row">
      <input
        id={inputId}
        type="checkbox"
        className="checkbox"
        aria-describedby={description ? describeId : undefined}
        {...rest}
      />
      <div className="checkbox-text">
        <label htmlFor={inputId}>{label}</label>
        {description ? (
          <p className="field-hint" id={describeId}>
            {description}
          </p>
        ) : null}
      </div>
    </div>
  );
}

/* ------------------------------------------------------------------ */
/* Surfaces                                                            */
/* ------------------------------------------------------------------ */

export function Card({
  title,
  actions,
  children,
  className,
}: {
  title?: ReactNode;
  actions?: ReactNode;
  children: ReactNode;
  className?: string;
}) {
  return (
    <section className={['card', className ?? ''].filter(Boolean).join(' ')}>
      {title || actions ? (
        <header className="card-header">
          {title ? <h2 className="card-title">{title}</h2> : null}
          {actions ? <div className="card-actions">{actions}</div> : null}
        </header>
      ) : null}
      <div className="card-body">{children}</div>
    </section>
  );
}

export function Badge({ tone = 'neutral', children }: { tone?: 'neutral' | 'success' | 'danger' | 'warning' | 'info'; children: ReactNode }) {
  return <span className={`badge badge-${tone}`}>{children}</span>;
}

const SUCCESS_STATUSES = new Set(['stored', 'completed', 'published', 'active', 'healthy', 'ok']);
const WARNING_STATUSES = new Set(['duplicate', 'paused']);
const DANGER_STATUSES = new Set(['failed', 'rejected', 'deadletter', 'degraded', 'revoked']);
const INFO_STATUSES = new Set(['pending', 'processing', 'validating', 'publishing', 'running']);

export function StatusBadge({ status }: { status: string | null | undefined }) {
  if (!status) return <Badge>—</Badge>;
  const normalized = status.toLowerCase();
  const tone = SUCCESS_STATUSES.has(normalized)
    ? 'success'
    : WARNING_STATUSES.has(normalized)
      ? 'warning'
      : DANGER_STATUSES.has(normalized)
        ? 'danger'
        : INFO_STATUSES.has(normalized)
          ? 'info'
          : 'neutral';
  return <Badge tone={tone}>{status}</Badge>;
}

/* ------------------------------------------------------------------ */
/* States                                                              */
/* ------------------------------------------------------------------ */

export function Spinner({ label }: { label?: string }) {
  return (
    <span className="spinner-wrap" role={label ? 'status' : undefined} aria-live={label ? 'polite' : undefined}>
      <span className="spinner" aria-hidden="true" />
      {label ? <span className="sr-only">{label}</span> : null}
    </span>
  );
}

export function LoadingState({ label }: { label?: string }) {
  const { t } = useUi();
  return (
    <div className="state-block" role="status" aria-live="polite">
      <span className="spinner" aria-hidden="true" />
      <p>{label ?? t('common.loading')}</p>
    </div>
  );
}

export function EmptyState({
  title,
  body,
  action,
  icon = 'music',
}: {
  title: string;
  body?: string;
  action?: ReactNode;
  icon?: IconName;
}) {
  return (
    <div className="state-block empty-state">
      <span className="state-icon" aria-hidden="true">
        <Icon name={icon} size={28} />
      </span>
      <h3>{title}</h3>
      {body ? <p>{body}</p> : null}
      {action ? <div className="state-action">{action}</div> : null}
    </div>
  );
}

export function ErrorState({ error, onRetry, title }: { error: unknown; onRetry?: () => void; title?: string }) {
  const { t } = useUi();
  let message = t('errors.unexpected');
  if (error instanceof ApiError) {
    if (error.isNetworkError) message = t('errors.network');
    else if (error.status === 401) message = t('auth.sessionExpired');
    else if (error.status === 403) message = t('errors.forbiddenBody');
    else if (error.status === 404) message = t('errors.notFoundBody');
    else message = error.message;
  } else if (typeof error === 'string' && error) {
    message = error;
  }
  return (
    <div className="state-block error-state" role="alert">
      <span className="state-icon" aria-hidden="true">
        <Icon name="warning" size={26} />
      </span>
      <h3>{title ?? t('errors.loadFailed')}</h3>
      <p>{message}</p>
      {onRetry ? (
        <div className="state-action">
          <Button icon="refresh" onClick={onRetry}>
            {t('common.tryAgain')}
          </Button>
        </div>
      ) : null}
    </div>
  );
}

export function Toolbar({ children }: { children: ReactNode }) {
  return <div className="toolbar">{children}</div>;
}

export function PageHeader({
  title,
  subtitle,
  actions,
}: {
  title: string;
  subtitle?: string;
  actions?: ReactNode;
}) {
  return (
    <div className="page-header">
      <div>
        <h1 className="page-title">{title}</h1>
        {subtitle ? <p className="page-subtitle">{subtitle}</p> : null}
      </div>
      {actions ? <div className="page-actions">{actions}</div> : null}
    </div>
  );
}

export function DefinitionList({ items }: { items: { label: string; value: ReactNode }[] }) {
  return (
    <dl className="definition-list">
      {items.map((item) => (
        <div key={item.label} className="definition-row">
          <dt>{item.label}</dt>
          <dd>{item.value}</dd>
        </div>
      ))}
    </dl>
  );
}
