import { useEffect, useMemo, useRef, useState, type FormEvent } from 'react';
import QRCode from 'qrcode';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../api/endpoints';
import type { RoleName, UserSummary } from '../api/types';
import { useAuth } from '../auth/AuthProvider';
import { useUi } from '../ui/UiProvider';
import { Modal, useToast } from '../ui/Modal';
import {
  Badge,
  Button,
  Checkbox,
  EmptyState,
  ErrorState,
  Field,
  IconButton,
  Input,
  LoadingState,
  PageHeader,
  Select,
  Toolbar,
} from '../ui/primitives';
import { errorMessage, roleLabel } from '../ui/helpers';
import { Icon } from '../ui/icons';

const GB = 1024 * 1024 * 1024;
const HOUR_SECONDS = 3600;
const ROLES: RoleName[] = ['Administrator', 'Contributor', 'Listener'];

interface CreateForm {
  email: string;
  displayName: string;
  password: string;
  role: RoleName;
  quotaGb: string;
  listenHours: string;
  expiresOn: string;
  canUsePlayer: boolean;
}

interface EditForm {
  displayName: string;
  role: RoleName;
  isActive: boolean;
  quotaGb: string;
  listenHours: string;
  expiresOn: string;
  clearExpiry: boolean;
  resetListenUsage: boolean;
  canUsePlayer: boolean;
}

const EMPTY_CREATE: CreateForm = {
  email: '',
  displayName: '',
  password: '',
  role: 'Listener',
  quotaGb: '10',
  listenHours: '0',
  expiresOn: '',
  canUsePlayer: true,
};

function gbFromBytes(bytes: number): string {
  const value = bytes / GB;
  return Number.isFinite(value) ? String(Math.round(value * 100) / 100) : '0';
}

function bytesFromGb(value: string): number | null {
  const parsed = Number(value.trim());
  if (!Number.isFinite(parsed) || parsed < 0) return null;
  return Math.round(parsed * GB);
}

function hoursFromSeconds(seconds: number): string {
  if (!seconds) return '0';
  return String(Math.round((seconds / HOUR_SECONDS) * 100) / 100);
}

function secondsFromHours(value: string): number | null {
  const parsed = Number(value.trim());
  if (!Number.isFinite(parsed) || parsed < 0) return null;
  return Math.round(parsed * HOUR_SECONDS);
}

/** Converts a stored timestamp to the local `yyyy-mm-dd` value used by `<input type="date">`. */
function dateInputFromIso(iso: string | null): string {
  if (!iso) return '';
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return '';
  const pad = (value: number) => String(value).padStart(2, '0');
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

/** Expiry applies until the end of the selected local day. */
function isoFromDateInput(value: string): string | null {
  if (!value) return null;
  const date = new Date(`${value}T23:59:59`);
  return Number.isNaN(date.getTime()) ? null : date.toISOString();
}

function deactivationLabelKey(reason: string | null) {
  switch (reason) {
    case 'expired':
      return 'users.reasonExpired' as const;
    case 'storage_quota':
      return 'users.reasonStorage' as const;
    case 'listening_quota':
      return 'users.reasonListening' as const;
    case 'manual':
      return 'users.reasonManual' as const;
    default:
      return null;
  }
}

export function UsersPage() {
  const { t, formatNumber, formatDate, formatBytes } = useUi();
  const { user: currentUser, refreshUser } = useAuth();
  const { notify } = useToast();
  const queryClient = useQueryClient();

  const [query, setQuery] = useState('');
  const [createOpen, setCreateOpen] = useState(false);
  const [createForm, setCreateForm] = useState<CreateForm>(EMPTY_CREATE);
  const [createError, setCreateError] = useState<string | null>(null);
  const [editing, setEditing] = useState<UserSummary | null>(null);
  const [editForm, setEditForm] = useState<EditForm | null>(null);
  const [editError, setEditError] = useState<string | null>(null);
  const [resetting, setResetting] = useState<UserSummary | null>(null);
  const [newPassword, setNewPassword] = useState('');
  const [resetError, setResetError] = useState<string | null>(null);
  const [historyFor, setHistoryFor] = useState<UserSummary | null>(null);

  // QR Code fast pairing modal state
  const [qrUser, setQrUser] = useState<UserSummary | null>(null);
  const [qrData, setQrData] = useState<{
    qrPayload: string;
    publicServerUrl: string | null;
    lanServerUrl: string | null;
    effectiveServerUrl: string;
    expiresAt: string;
    deviceName: string;
  } | null>(null);
  const [qrLoading, setQrLoading] = useState(false);
  const [qrDeviceName, setQrDeviceName] = useState('');
  const [copiedImage, setCopiedImage] = useState(false);
  const canvasRef = useRef<HTMLCanvasElement | null>(null);

  const openQrModal = async (target: UserSummary, customDeviceName?: string) => {
    setQrUser(target);
    const initialDevice = customDeviceName || `${target.displayName}'s Android Device`;
    setQrDeviceName(initialDevice);
    setQrLoading(true);
    setCopiedImage(false);
    try {
      const res = await api.users.pairingQr(target.id, { deviceName: initialDevice });
      setQrData({
        qrPayload: res.qrPayload,
        publicServerUrl: res.publicServerUrl,
        lanServerUrl: res.lanServerUrl,
        effectiveServerUrl: res.effectiveServerUrl,
        expiresAt: res.expiresAt,
        deviceName: res.deviceName,
      });
    } catch (cause) {
      notify(errorMessage(cause, t('common.error')), 'error');
    } finally {
      setQrLoading(false);
    }
  };

  useEffect(() => {
    if (!qrData?.qrPayload || !canvasRef.current) return;
    void QRCode.toCanvas(canvasRef.current, qrData.qrPayload, {
      width: 280,
      margin: 2,
      color: {
        dark: '#000000',
        light: '#ffffff',
      },
    });
  }, [qrData]);

  const copyQrImage = () => {
    const canvas = canvasRef.current;
    if (!canvas) return;
    canvas.toBlob(async (blob) => {
      if (!blob) return;
      try {
        await navigator.clipboard.write([
          new ClipboardItem({ 'image/png': blob }),
        ]);
        setCopiedImage(true);
        notify(t('users.qrImageCopied'), 'success');
        setTimeout(() => setCopiedImage(false), 3000);
      } catch (err) {
        notify(errorMessage(err, 'Failed to copy image to clipboard'), 'error');
      }
    }, 'image/png');
  };

  const downloadQrImage = () => {
    const canvas = canvasRef.current;
    if (!canvas || !qrUser) return;
    const a = document.createElement('a');
    a.download = `irpotify-qr-${qrUser.email}.png`;
    a.href = canvas.toDataURL('image/png');
    a.click();
  };

  const shareQr = async () => {
    if (!qrData || !qrUser) return;
    const title = `irpotify Login QR - ${qrUser.displayName}`;
    const text = `irpotify Fast Login for ${qrUser.displayName}:\n${qrData.qrPayload}`;
    if (navigator.share) {
      try {
        await navigator.share({ title, text, url: qrData.qrPayload });
      } catch {
        // Ignored or cancelled by user
      }
    } else {
      await navigator.clipboard.writeText(qrData.qrPayload);
      notify(t('users.qrImageCopied'), 'success');
    }
  };

  const usersQuery = useQuery({ queryKey: ['users'], queryFn: api.users.list });
  const historyQuery = useQuery({
    queryKey: ['users', historyFor?.id, 'login-history'],
    queryFn: () => api.users.loginHistory(historyFor!.id),
    enabled: Boolean(historyFor),
  });

  const roleOptions = useMemo(
    () => ROLES.map((role) => ({ value: role, label: roleLabel(t, role) })),
    [t],
  );

  const filtered = useMemo(() => {
    const needle = query.trim().toLowerCase();
    const users = usersQuery.data ?? [];
    if (!needle) return users;
    return users.filter(
      (user) => user.email.toLowerCase().includes(needle) || user.displayName.toLowerCase().includes(needle),
    );
  }, [usersQuery.data, query]);

  const isSelf = (user: UserSummary) => currentUser?.id === user.id;

  const hoursLabel = (seconds: number) =>
    t('users.hoursValue', { value: formatNumber(Math.round((seconds / HOUR_SECONDS) * 10) / 10) });
  const isExpired = (iso: string) => new Date(iso).getTime() <= Date.now();

  const createMutation = useMutation({
    mutationFn: (form: CreateForm) => {
      const quotaBytes = bytesFromGb(form.quotaGb);
      if (quotaBytes === null) throw new Error(t('settings.invalidNumbers'));
      const listenQuotaSeconds = secondsFromHours(form.listenHours);
      if (listenQuotaSeconds === null) throw new Error(t('settings.invalidNumbers'));
      return api.users.create({
        email: form.email.trim(),
        password: form.password,
        displayName: form.displayName.trim(),
        role: form.role,
        quotaBytes,
        canUsePlayer: form.canUsePlayer,
        expiresAt: isoFromDateInput(form.expiresOn),
        listenQuotaSeconds,
      });
    },
    onSuccess: (data, variables) => {
      void queryClient.invalidateQueries({ queryKey: ['users'] });
      setCreateOpen(false);
      setCreateForm(EMPTY_CREATE);
      notify(t('users.created'), 'success');
      void openQrModal({
        id: data.id,
        email: variables.email.trim(),
        displayName: variables.displayName.trim(),
        roles: [variables.role],
        isActive: true,
        quotaBytes: 0,
        storageUsedBytes: 0,
        canUsePlayer: variables.canUsePlayer,
        listenQuotaSeconds: 0,
        listenSecondsUsed: 0,
        expiresAt: null,
        deactivatedAt: null,
        deactivationReason: null,
        createdAt: new Date().toISOString(),
        lastLoginAt: null,
      });
    },
    onError: (error) => setCreateError(errorMessage(error, t('common.error'))),
  });

  const updateMutation = useMutation({
    mutationFn: ({ target, form }: { target: UserSummary; form: EditForm }) => {
      const quotaBytes = bytesFromGb(form.quotaGb);
      if (quotaBytes === null) throw new Error(t('settings.invalidNumbers'));
      const listenQuotaSeconds = secondsFromHours(form.listenHours);
      if (listenQuotaSeconds === null) throw new Error(t('settings.invalidNumbers'));
      const expiry: { expiresAt?: string; clearExpiresAt?: boolean } = form.clearExpiry
        ? { clearExpiresAt: true }
        : form.expiresOn
          ? { expiresAt: isoFromDateInput(form.expiresOn) ?? undefined }
          : {};
      return api.users.update(target.id, {
        displayName: form.displayName.trim(),
        quotaBytes,
        canUsePlayer: form.canUsePlayer,
        listenQuotaSeconds,
        ...(form.resetListenUsage ? { resetListenUsage: true } : {}),
        ...expiry,
        ...(isSelf(target) ? {} : { role: form.role, isActive: form.isActive }),
      });
    },
    onSuccess: async (_result, variables) => {
      void queryClient.invalidateQueries({ queryKey: ['users'] });
      if (isSelf(variables.target)) await refreshUser();
      setEditing(null);
      setEditForm(null);
      notify(t('users.updated'), 'success');
    },
    onError: (error) => setEditError(errorMessage(error, t('common.error'))),
  });

  const reactivateMutation = useMutation({
    mutationFn: (target: UserSummary) => api.users.update(target.id, { isActive: true }),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['users'] });
      notify(t('users.reactivated'), 'success');
    },
    onError: (error) => notify(errorMessage(error, t('common.error')), 'error'),
  });

  const resetMutation = useMutation({
    mutationFn: ({ target, password }: { target: UserSummary; password: string }) =>
      api.users.resetPassword(target.id, password),
    onSuccess: () => {
      setResetting(null);
      setNewPassword('');
      notify(t('users.passwordReset'), 'success');
    },
    onError: (error) => setResetError(errorMessage(error, t('common.error'))),
  });

  const openEdit = (target: UserSummary) => {
    setEditing(target);
    setEditForm({
      displayName: target.displayName,
      role: target.roles[0] ?? 'Listener',
      isActive: target.isActive,
      quotaGb: gbFromBytes(target.quotaBytes),
      listenHours: hoursFromSeconds(target.listenQuotaSeconds),
      expiresOn: dateInputFromIso(target.expiresAt),
      clearExpiry: false,
      resetListenUsage: false,
      canUsePlayer: target.canUsePlayer ?? true,
    });
    setEditError(null);
  };

  const submitCreate = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const email = createForm.email.trim();
    if (!email) {
      setCreateError(t('common.requiredField'));
      return;
    }
    if (createForm.password.length < 12) {
      setCreateError(t('users.passwordHint'));
      return;
    }
    if (bytesFromGb(createForm.quotaGb) === null || secondsFromHours(createForm.listenHours) === null) {
      setCreateError(t('settings.invalidNumbers'));
      return;
    }
    createMutation.mutate({ ...createForm, email });
  };

  const submitEdit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!editing || !editForm) return;
    if (!editForm.displayName.trim()) {
      setEditError(t('common.requiredField'));
      return;
    }
    if (bytesFromGb(editForm.quotaGb) === null || secondsFromHours(editForm.listenHours) === null) {
      setEditError(t('settings.invalidNumbers'));
      return;
    }
    updateMutation.mutate({ target: editing, form: editForm });
  };

  const submitReset = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!resetting) return;
    if (newPassword.length < 12) {
      setResetError(t('users.passwordHint'));
      return;
    }
    resetMutation.mutate({ target: resetting, password: newPassword });
  };

  return (
    <div className="page">
      <PageHeader
        title={t('users.title')}
        subtitle={t('users.subtitle')}
        actions={
          <>
            <Button icon="refresh" loading={usersQuery.isFetching} onClick={() => void usersQuery.refetch()}>
              {t('common.refresh')}
            </Button>
            <Button
              variant="primary"
              icon="plus"
              onClick={() => {
                setCreateForm(EMPTY_CREATE);
                setCreateError(null);
                setCreateOpen(true);
              }}
            >
              {t('users.create')}
            </Button>
          </>
        }
      />

      <Toolbar>
        <div className="search-field">
          <Icon name="search" size={18} />
          <Input
            type="search"
            value={query}
            onChange={(event) => setQuery(event.target.value)}
            placeholder={t('users.search')}
            aria-label={t('users.search')}
          />
        </div>
        <span className="muted small">
          {t('common.showing', {
            from: formatNumber(filtered.length === 0 ? 0 : 1),
            to: formatNumber(filtered.length),
            total: formatNumber(usersQuery.data?.length ?? 0),
          })}
        </span>
      </Toolbar>

      {usersQuery.isLoading ? <LoadingState /> : null}
      {usersQuery.isError ? (
        <ErrorState error={usersQuery.error} title={t('users.loadFailed')} onRetry={() => void usersQuery.refetch()} />
      ) : null}

      {usersQuery.data ? (
        filtered.length === 0 ? (
          <EmptyState icon="users" title={t('users.empty')} />
        ) : (
          <div className="table-wrap">
            <table className="data-table">
              <thead>
                <tr>
                  <th scope="col">{t('users.displayName')}</th>
                  <th scope="col">{t('users.email')}</th>
                  <th scope="col">{t('users.role')}</th>
                  <th scope="col">{t('common.status')}</th>
                  <th scope="col">{t('users.playerAccess')}</th>
                  <th scope="col">{t('users.usage')}</th>
                  <th scope="col">{t('users.expires')}</th>
                  <th scope="col">{t('common.lastLogin')}</th>
                  <th scope="col">{t('common.actions')}</th>
                </tr>
              </thead>
              <tbody>
                {filtered.map((target) => (
                  <tr key={target.id}>
                    <td>
                      <span>{target.displayName}</span>
                      {isSelf(target) ? (
                        <span className="inline-badge">
                          <Badge tone="info">{t('users.you')}</Badge>
                        </span>
                      ) : null}
                    </td>
                    <td className="mono small">{target.email}</td>
                    <td>{target.roles.map((role) => <Badge key={role} tone={role === 'Administrator' ? 'warning' : 'info'}>{roleLabel(t, role)}</Badge>)}</td>
                    <td>
                      {target.isActive ? (
                        <Badge tone="success">{t('common.active')}</Badge>
                      ) : (
                        <>
                          <Badge tone="danger">{t('common.inactive')}</Badge>
                          {deactivationLabelKey(target.deactivationReason) ? (
                            <div className="muted small">
                              {t(deactivationLabelKey(target.deactivationReason)!)}
                            </div>
                          ) : null}
                        </>
                      )}
                    </td>
                    <td>
                      {target.canUsePlayer ? (
                        <Badge tone="info">{t('common.yes')}</Badge>
                      ) : (
                        <Badge tone="neutral">{t('common.no')}</Badge>
                      )}
                    </td>
                    <td className="small">
                      <div>
                        <span className="muted">{t('users.storageUsage')}: </span>
                        {formatBytes(target.storageUsedBytes)} /{' '}
                        {target.quotaBytes > 0 ? formatBytes(target.quotaBytes) : t('users.unlimited')}
                      </div>
                      <div>
                        <span className="muted">{t('users.listenUsage')}: </span>
                        {hoursLabel(target.listenSecondsUsed)} /{' '}
                        {target.listenQuotaSeconds > 0 ? hoursLabel(target.listenQuotaSeconds) : t('users.unlimited')}
                      </div>
                    </td>
                    <td className="small">
                      {target.expiresAt ? (
                        <span className={isExpired(target.expiresAt) ? 'text-danger' : undefined}>
                          {formatDate(target.expiresAt, { dateStyle: 'medium' })}
                        </span>
                      ) : (
                        <span className="muted">{t('users.noExpiry')}</span>
                      )}
                    </td>
                    <td className="muted small">{formatDate(target.lastLoginAt)}</td>
                    <td>
                      <div className="row-actions">
                        <IconButton icon="edit" label={t('users.edit')} onClick={() => openEdit(target)} />
                        {!target.isActive && !isSelf(target) ? (
                          <IconButton
                            icon="play"
                            label={t('users.reactivate')}
                            onClick={() => reactivateMutation.mutate(target)}
                          />
                        ) : null}
                        <IconButton
                          icon="shield"
                          label={t('users.resetPassword')}
                          onClick={() => {
                            setResetting(target);
                            setNewPassword('');
                            setResetError(null);
                          }}
                        />
                        <IconButton icon="sessions" label={t('users.historyTitle')} onClick={() => setHistoryFor(target)} />
                        <IconButton icon="qr" label={t('users.qrCode')} onClick={() => void openQrModal(target)} />
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )
      ) : null}

      <Modal
        open={createOpen}
        title={t('users.create')}
        onClose={() => setCreateOpen(false)}
        footer={
          <>
            <Button onClick={() => setCreateOpen(false)}>{t('common.cancel')}</Button>
            <Button variant="primary" type="submit" form="user-create-form" loading={createMutation.isPending}>
              {createMutation.isPending ? t('common.saving') : t('common.create')}
            </Button>
          </>
        }
      >
        {createError ? (
          <div className="notice notice-error" role="alert">
            {createError}
          </div>
        ) : null}
        <form id="user-create-form" className="form" onSubmit={submitCreate} noValidate>
          <Field label={t('users.email')} htmlFor="user-email" required>
            <Input
              id="user-email"
              type="email"
              autoComplete="off"
              value={createForm.email}
              onChange={(event) => setCreateForm((form) => ({ ...form, email: event.target.value }))}
              required
            />
          </Field>
          <Field label={t('users.displayName')} htmlFor="user-display-name">
            <Input
              id="user-display-name"
              value={createForm.displayName}
              onChange={(event) => setCreateForm((form) => ({ ...form, displayName: event.target.value }))}
            />
          </Field>
          <Field label={t('users.password')} htmlFor="user-password" hint={t('users.passwordHint')} required>
            <Input
              id="user-password"
              type="password"
              autoComplete="new-password"
              value={createForm.password}
              onChange={(event) => setCreateForm((form) => ({ ...form, password: event.target.value }))}
              required
            />
          </Field>
          <div className="form-grid two">
            <Field label={t('users.role')} htmlFor="user-role">
              <Select
                id="user-role"
                options={roleOptions}
                value={createForm.role}
                onChange={(event) => setCreateForm((form) => ({ ...form, role: event.target.value as RoleName }))}
              />
            </Field>
            <Field label={t('users.quota')} htmlFor="user-quota" hint={t('users.quotaHint')}>
              <Input
                id="user-quota"
                type="number"
                min={0}
                step="0.5"
                value={createForm.quotaGb}
                onChange={(event) => setCreateForm((form) => ({ ...form, quotaGb: event.target.value }))}
              />
            </Field>
          </div>
          <div className="form-grid two">
            <Field label={t('users.expiresAt')} htmlFor="user-expires" hint={t('users.expiresHint')}>
              <Input
                id="user-expires"
                type="date"
                value={createForm.expiresOn}
                onChange={(event) => setCreateForm((form) => ({ ...form, expiresOn: event.target.value }))}
              />
            </Field>
            <Field label={t('users.listenQuota')} htmlFor="user-listen" hint={t('users.listenQuotaHint')}>
              <Input
                id="user-listen"
                type="number"
                min={0}
                step="0.5"
                value={createForm.listenHours}
                onChange={(event) => setCreateForm((form) => ({ ...form, listenHours: event.target.value }))}
              />
            </Field>
          </div>
          <Checkbox
            label={t('users.canUsePlayer')}
            description={t('users.canUsePlayerHint')}
            checked={createForm.canUsePlayer}
            onChange={(event) => setCreateForm((form) => ({ ...form, canUsePlayer: event.target.checked }))}
          />
        </form>
      </Modal>

      <Modal
        open={editing !== null && editForm !== null}
        title={t('users.edit')}
        onClose={() => {
          setEditing(null);
          setEditForm(null);
        }}
        footer={
          <>
            <Button
              onClick={() => {
                setEditing(null);
                setEditForm(null);
              }}
            >
              {t('common.cancel')}
            </Button>
            <Button variant="primary" type="submit" form="user-edit-form" loading={updateMutation.isPending}>
              {updateMutation.isPending ? t('common.saving') : t('common.save')}
            </Button>
          </>
        }
      >
        {editError ? (
          <div className="notice notice-error" role="alert">
            {editError}
          </div>
        ) : null}
        <p className="muted small mono">{editing?.email}</p>
        {editing && isSelf(editing) ? <div className="notice notice-info">{t('users.selfRoleHint')}</div> : null}
        <form id="user-edit-form" className="form" onSubmit={submitEdit} noValidate>
          <Field label={t('users.displayName')} htmlFor="edit-display-name" required>
            <Input
              id="edit-display-name"
              value={editForm?.displayName ?? ''}
              onChange={(event) => setEditForm((form) => (form ? { ...form, displayName: event.target.value } : form))}
              required
            />
          </Field>
          <div className="form-grid two">
            <Field label={t('users.role')} htmlFor="edit-role">
              <Select
                id="edit-role"
                options={roleOptions}
                value={editForm?.role ?? 'Listener'}
                disabled={Boolean(editing && isSelf(editing))}
                onChange={(event) => setEditForm((form) => (form ? { ...form, role: event.target.value as RoleName } : form))}
              />
            </Field>
            <Field label={t('users.quota')} htmlFor="edit-quota" hint={t('users.quotaHint')}>
              <Input
                id="edit-quota"
                type="number"
                min={0}
                step="0.5"
                value={editForm?.quotaGb ?? ''}
                onChange={(event) => setEditForm((form) => (form ? { ...form, quotaGb: event.target.value } : form))}
              />
            </Field>
          </div>
          <div className="form-grid two">
            <Field label={t('users.expiresAt')} htmlFor="edit-expires" hint={t('users.expiresHint')}>
              <Input
                id="edit-expires"
                type="date"
                disabled={editForm?.clearExpiry ?? false}
                value={editForm?.expiresOn ?? ''}
                onChange={(event) => setEditForm((form) => (form ? { ...form, expiresOn: event.target.value } : form))}
              />
            </Field>
            <Field label={t('users.listenQuota')} htmlFor="edit-listen" hint={t('users.listenQuotaHint')}>
              <Input
                id="edit-listen"
                type="number"
                min={0}
                step="0.5"
                value={editForm?.listenHours ?? ''}
                onChange={(event) => setEditForm((form) => (form ? { ...form, listenHours: event.target.value } : form))}
              />
            </Field>
          </div>
          <Checkbox
            label={t('users.clearExpiry')}
            checked={editForm?.clearExpiry ?? false}
            onChange={(event) => setEditForm((form) => (form ? { ...form, clearExpiry: event.target.checked } : form))}
          />
          <Checkbox
            label={t('users.resetListenUsage')}
            description={t('users.resetListenUsageHint')}
            checked={editForm?.resetListenUsage ?? false}
            onChange={(event) => setEditForm((form) => (form ? { ...form, resetListenUsage: event.target.checked } : form))}
          />
          <Checkbox
            label={t('users.active')}
            description={t('users.deactivateWarning')}
            checked={editForm?.isActive ?? true}
            disabled={Boolean(editing && isSelf(editing))}
            onChange={(event) => setEditForm((form) => (form ? { ...form, isActive: event.target.checked } : form))}
          />
          <Checkbox
            label={t('users.canUsePlayer')}
            description={t('users.canUsePlayerHint')}
            checked={editForm?.canUsePlayer ?? true}
            onChange={(event) => setEditForm((form) => (form ? { ...form, canUsePlayer: event.target.checked } : form))}
          />
        </form>
      </Modal>

      <Modal
        open={resetting !== null}
        title={t('users.resetTitle', { email: resetting?.email ?? '' })}
        onClose={() => setResetting(null)}
        footer={
          <>
            <Button onClick={() => setResetting(null)}>{t('common.cancel')}</Button>
            <Button variant="danger" type="submit" form="user-reset-form" loading={resetMutation.isPending}>
              {resetMutation.isPending ? t('common.saving') : t('users.resetPassword')}
            </Button>
          </>
        }
      >
        <p className="muted small">{t('users.resetBody')}</p>
        {resetError ? (
          <div className="notice notice-error" role="alert">
            {resetError}
          </div>
        ) : null}
        <form id="user-reset-form" className="form" onSubmit={submitReset} noValidate>
          <Field label={t('users.newPassword')} htmlFor="reset-password" hint={t('users.passwordHint')} required>
            <Input
              id="reset-password"
              type="password"
              autoComplete="new-password"
              value={newPassword}
              onChange={(event) => setNewPassword(event.target.value)}
              required
            />
          </Field>
        </form>
      </Modal>

      <Modal
        open={historyFor !== null}
        title={t('users.historyTitle')}
        onClose={() => setHistoryFor(null)}
        size="lg"
      >
        <p className="muted small mono">{historyFor?.email}</p>
        {historyQuery.isLoading ? <LoadingState /> : null}
        {historyQuery.isError ? <ErrorState error={historyQuery.error} onRetry={() => void historyQuery.refetch()} /> : null}
        {historyQuery.data ? (
          historyQuery.data.length === 0 ? (
            <EmptyState icon="sessions" title={t('users.historyEmpty')} />
          ) : (
            <div className="table-wrap">
              <table className="data-table">
                <thead>
                  <tr>
                    <th scope="col">{t('common.status')}</th>
                    <th scope="col">{t('users.ip')}</th>
                    <th scope="col">{t('users.userAgent')}</th>
                    <th scope="col">{t('users.reason')}</th>
                    <th scope="col">{t('audit.time')}</th>
                  </tr>
                </thead>
                <tbody>
                  {historyQuery.data.map((entry) => (
                    <tr key={entry.id}>
                      <td>
                        {entry.success ? (
                          <Badge tone="success">{t('users.success')}</Badge>
                        ) : (
                          <Badge tone="danger">{t('users.failure')}</Badge>
                        )}
                      </td>
                      <td className="mono small">{entry.ipAddress ?? '—'}</td>
                      <td className="small" title={entry.userAgent ?? ''}>
                        {entry.userAgent ?? '—'}
                      </td>
                      <td className="muted small">{entry.failureReason ?? '—'}</td>
                      <td className="muted small">{formatDate(entry.createdAt)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )
        ) : null}
      </Modal>

      <Modal
        open={qrUser !== null}
        title={t('users.qrModalTitle', { name: qrUser?.displayName || qrUser?.email || '' })}
        onClose={() => setQrUser(null)}
        size="md"
        footer={
          <Button variant="secondary" onClick={() => setQrUser(null)}>
            {t('common.close')}
          </Button>
        }
      >
        <p style={{ margin: '0 0 1rem', color: 'var(--text-muted)', fontSize: '13px' }}>
          {t('users.qrModalSubtitle')}
        </p>

        {qrLoading ? (
          <LoadingState />
        ) : qrData ? (
          <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: '1rem' }}>
            {/* White card container for crisp QR scanning */}
            <div
              style={{
                background: '#ffffff',
                padding: '16px',
                borderRadius: '16px',
                boxShadow: '0 4px 20px rgba(0,0,0,0.15)',
                display: 'inline-flex',
                justifyContent: 'center',
                alignItems: 'center',
              }}
            >
              <canvas ref={canvasRef} style={{ display: 'block', maxWidth: '100%', height: 'auto' }} />
            </div>

            {/* Quick Actions Row */}
            <div style={{ display: 'flex', flexWrap: 'wrap', gap: '8px', justifyContent: 'center', width: '100%' }}>
              <Button
                variant={copiedImage ? 'primary' : 'secondary'}
                size="sm"
                icon="check"
                onClick={copyQrImage}
              >
                {copiedImage ? t('common.confirm') : t('users.copyQrImage')}
              </Button>
              <Button variant="secondary" size="sm" icon="external" onClick={() => void shareQr()}>
                {t('users.shareQr')}
              </Button>
              <Button variant="secondary" size="sm" icon="arrow-down" onClick={downloadQrImage}>
                {t('users.downloadQr')}
              </Button>
            </div>

            {/* Direct Messenger Share Links */}
            <div style={{ display: 'flex', flexWrap: 'wrap', gap: '8px', alignItems: 'center', justifyContent: 'center' }}>
              <span style={{ fontSize: '12px', color: 'var(--text-muted)' }}>{t('users.shareQr')}:</span>
              <a
                href={`https://t.me/share/url?url=${encodeURIComponent(qrData.qrPayload)}&text=${encodeURIComponent('irpotify Fast Login for ' + (qrUser?.displayName || 'user'))}`}
                target="_blank"
                rel="noreferrer"
                className="btn btn-secondary btn-sm"
                style={{ textDecoration: 'none', fontSize: '12px', padding: '4px 10px' }}
              >
                {t('users.shareTelegram')}
              </a>
              <a
                href={`https://api.whatsapp.com/send?text=${encodeURIComponent('irpotify Fast Login link for ' + (qrUser?.displayName || '') + ':\n' + qrData.qrPayload)}`}
                target="_blank"
                rel="noreferrer"
                className="btn btn-secondary btn-sm"
                style={{ textDecoration: 'none', fontSize: '12px', padding: '4px 10px' }}
              >
                {t('users.shareWhatsApp')}
              </a>
              <a
                href={`mailto:?subject=${encodeURIComponent('Your irpotify Login QR Code')}&body=${encodeURIComponent('Open or scan this link in the irpotify Android app:\n' + qrData.qrPayload)}`}
                className="btn btn-secondary btn-sm"
                style={{ textDecoration: 'none', fontSize: '12px', padding: '4px 10px' }}
              >
                {t('users.shareEmail')}
              </a>
            </div>

            {/* Device Name Customization */}
            <div style={{ width: '100%', marginTop: '0.5rem', borderTop: '1px solid var(--border)', paddingTop: '1rem' }}>
              <Field label={t('users.deviceName')} htmlFor="qr-device-name" hint={t('users.deviceNameHint')}>
                <div style={{ display: 'flex', gap: '8px' }}>
                  <Input
                    id="qr-device-name"
                    value={qrDeviceName}
                    onChange={(e) => setQrDeviceName(e.target.value)}
                  />
                  <Button
                    variant="secondary"
                    size="sm"
                    icon="refresh"
                    onClick={() => qrUser && void openQrModal(qrUser, qrDeviceName)}
                  >
                    {t('users.regenerateQr')}
                  </Button>
                </div>
              </Field>
            </div>

            {/* Network Endpoints Information */}
            <div
              style={{
                width: '100%',
                background: 'var(--surface-2)',
                padding: '12px',
                borderRadius: '8px',
                fontSize: '12px',
                display: 'flex',
                flexDirection: 'column',
                gap: '6px',
              }}
            >
              <div style={{ fontWeight: 600, color: 'var(--text)' }}>{t('users.serverAddresses')}:</div>
              {qrData.lanServerUrl ? (
                <div>
                  <span style={{ color: 'var(--text-muted)' }}>{t('users.lanAddress')}: </span>
                  <code style={{ fontSize: '11px' }}>{qrData.lanServerUrl}</code>
                </div>
              ) : null}
              {qrData.publicServerUrl ? (
                <div>
                  <span style={{ color: 'var(--text-muted)' }}>{t('users.publicAddress')}: </span>
                  <code style={{ fontSize: '11px' }}>{qrData.publicServerUrl}</code>
                </div>
              ) : null}
              <div style={{ color: 'var(--text-muted)', fontSize: '11px', marginTop: '4px' }}>
                {t('users.qrNotice')}
              </div>
            </div>
          </div>
        ) : null}
      </Modal>

    </div>
  );
}
