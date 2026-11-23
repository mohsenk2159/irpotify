import { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { api } from '../api/endpoints';
import type { SystemNotification } from '../api/types';
import { useUi } from '../ui/UiProvider';
import { useToast, Modal } from '../ui/Modal';
import {
  Badge,
  Button,
  EmptyState,
  ErrorState,
  Field,
  Input,
  LoadingState,
  PageHeader,
} from '../ui/primitives';
import { ThreeDIcon } from '../ui/ThreeDIcons';
import { errorMessage } from '../ui/helpers';

export function NotificationsPage() {
  const { t, formatDate } = useUi();
  const { notify } = useToast();
  const queryClient = useQueryClient();

  const [filterStatus, setFilterStatus] = useState<'all' | 'unread' | 'read'>('all');
  const [sortOrder, setSortOrder] = useState<'newest' | 'oldest' | 'unreadFirst'>('newest');
  const [selectedIds, setSelectedIds] = useState<string[]>([]);

  // Password reset modal state
  const [resetTarget, setResetTarget] = useState<{
    notification: SystemNotification;
    userId: string;
    email: string;
  } | null>(null);
  const [newPassword, setNewPassword] = useState('');
  const [resetError, setResetError] = useState<string | null>(null);

  // Query notifications
  const notificationsQuery = useQuery({
    queryKey: ['notifications', filterStatus, sortOrder],
    queryFn: () =>
      api.notifications.list({
        status: filterStatus === 'all' ? undefined : filterStatus,
        sort: sortOrder,
        pageSize: 100,
      }),
    refetchInterval: 15000,
  });

  // Query users to map emails to userIds if not present on notification
  const usersQuery = useQuery({
    queryKey: ['users'],
    queryFn: api.users.list,
  });

  const invalidateNotifications = () => {
    void queryClient.invalidateQueries({ queryKey: ['notifications'] });
  };

  // Mark single as read
  const markReadMutation = useMutation({
    mutationFn: (id: string) => api.notifications.markRead(id),
    onSuccess: () => {
      invalidateNotifications();
    },
    onError: (err) => notify(errorMessage(err, 'خطا در ثبت وضعیت'), 'error'),
  });

  // Mark all as read
  const markAllReadMutation = useMutation({
    mutationFn: api.notifications.markAllRead,
    onSuccess: () => {
      invalidateNotifications();
      notify('تمامی پیام‌ها به عنوان خوانده‌شده علامت‌گذاری شدند.', 'success');
    },
    onError: (err) => notify(errorMessage(err, 'خطا در ثبت وضعیت'), 'error'),
  });

  // Single delete
  const deleteMutation = useMutation({
    mutationFn: (id: string) => api.notifications.delete(id),
    onSuccess: (_, id) => {
      setSelectedIds((prev) => prev.filter((item) => item !== id));
      invalidateNotifications();
      notify('پیام حذف شد.', 'success');
    },
    onError: (err) => notify(errorMessage(err, 'خطا در حذف پیام'), 'error'),
  });

  // Batch delete
  const batchDeleteMutation = useMutation({
    mutationFn: (ids: string[]) => api.notifications.batchDelete(ids),
    onSuccess: () => {
      setSelectedIds([]);
      invalidateNotifications();
      notify('پیام‌های انتخاب‌شده با موفقیت حذف شدند.', 'success');
    },
    onError: (err) => notify(errorMessage(err, 'خطا در حذف گروهی'), 'error'),
  });

  // Reset user password mutation
  const resetPasswordMutation = useMutation({
    mutationFn: ({ userId, password }: { userId: string; password: string }) =>
      api.users.resetPassword(userId, password),
    onSuccess: () => {
      if (resetTarget?.notification.id) {
        // Also mark the notification as read
        void api.notifications.markRead(resetTarget.notification.id);
      }
      invalidateNotifications();
      notify(`رمز عبور جدید برای کاربر ${resetTarget?.email} با موفقیت تنظیم شد.`, 'success');
      setResetTarget(null);
      setNewPassword('');
      setResetError(null);
    },
    onError: (err) => setResetError(errorMessage(err, 'خطا در تنظیم رمز عبور')),
  });

  const items = notificationsQuery.data?.items ?? [];
  const unreadCount = notificationsQuery.data?.unreadCount ?? 0;
  const totalCount = notificationsQuery.data?.totalCount ?? 0;

  const handleSelectAll = (checked: boolean) => {
    if (checked) {
      setSelectedIds(items.map((i) => i.id));
    } else {
      setSelectedIds([]);
    }
  };

  const toggleSelectOne = (id: string) => {
    setSelectedIds((prev) =>
      prev.includes(id) ? prev.filter((item) => item !== id) : [...prev, id]
    );
  };

  const handleOpenResetPassword = (notification: SystemNotification) => {
    let targetUserId = notification.userId;
    const email = notification.email || '';

    // If userId not stored on notification, look up in users list
    if (!targetUserId && usersQuery.data && email) {
      const found = usersQuery.data.find(
        (u) => u.email.toLowerCase() === email.toLowerCase()
      );
      if (found) {
        targetUserId = found.id;
      }
    }

    if (!targetUserId) {
      notify(`کاربری با ایمیل ${email} در سیستم یافت نشد.`, 'error');
      return;
    }

    setResetTarget({ notification, userId: targetUserId, email });
    setNewPassword('');
    setResetError(null);
  };

  const generateRandomPassword = () => {
    const chars = 'abcdefghjkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789!@#$%&*';
    let pass = 'Ir!';
    for (let i = 0; i < 12; i++) {
      pass += chars.charAt(Math.floor(Math.random() * chars.length));
    }
    setNewPassword(pass);
  };

  return (
    <div className="page">
      <PageHeader
        title="اعلانات و درخواست‌ها"
        subtitle={
          unreadCount > 0
            ? `${unreadCount} پیام خوانده‌نشده دارید`
            : 'همه اعلانات و درخواست‌های کاربران'
        }
        actions={
          <div style={{ display: 'flex', gap: '0.5rem', flexWrap: 'wrap' }}>
            {unreadCount > 0 && (
              <Button
                variant="secondary"
                icon="check"
                loading={markAllReadMutation.isPending}
                onClick={() => markAllReadMutation.mutate()}
              >
                خواندن همه
              </Button>
            )}
            {selectedIds.length > 0 && (
              <Button
                variant="danger"
                icon="trash"
                loading={batchDeleteMutation.isPending}
                onClick={() => {
                  if (confirm(`آیا از حذف ${selectedIds.length} پیام انتخاب‌شده اطمینان دارید؟`)) {
                    batchDeleteMutation.mutate(selectedIds);
                  }
                }}
              >
                حذف انتخاب‌شده‌ها ({selectedIds.length})
              </Button>
            )}
            <Button
              icon="refresh"
              loading={notificationsQuery.isFetching}
              onClick={() => void notificationsQuery.refetch()}
            >
              {t('common.refresh')}
            </Button>
          </div>
        }
      />

      {/* Filter and Sort Toolbar */}
      <div
        style={{
          display: 'flex',
          justifyContent: 'space-between',
          alignItems: 'center',
          gap: '1rem',
          flexWrap: 'wrap',
          marginBottom: '1rem',
          padding: '0.75rem 1rem',
          background: 'var(--surface-2)',
          borderRadius: '12px',
          border: '1px solid var(--border)',
        }}
      >
        <div style={{ display: 'flex', gap: '0.5rem', alignItems: 'center' }}>
          <span style={{ fontSize: '13px', fontWeight: 600, color: 'var(--text-muted)' }}>
            نمایش:
          </span>
          <button
            type="button"
            className={`btn btn-sm ${filterStatus === 'all' ? 'btn-primary' : 'btn-ghost'}`}
            onClick={() => setFilterStatus('all')}
          >
            همه ({totalCount})
          </button>
          <button
            type="button"
            className={`btn btn-sm ${filterStatus === 'unread' ? 'btn-primary' : 'btn-ghost'}`}
            onClick={() => setFilterStatus('unread')}
          >
            خوانده‌نشده ({unreadCount})
          </button>
          <button
            type="button"
            className={`btn btn-sm ${filterStatus === 'read' ? 'btn-primary' : 'btn-ghost'}`}
            onClick={() => setFilterStatus('read')}
          >
            خوانده‌شده
          </button>
        </div>

        <div style={{ display: 'flex', gap: '0.5rem', alignItems: 'center' }}>
          <label htmlFor="sort-select" style={{ fontSize: '13px', color: 'var(--text-muted)' }}>
            مرتب‌سازی:
          </label>
          <select
            id="sort-select"
            value={sortOrder}
            onChange={(e) => setSortOrder(e.target.value as 'newest' | 'oldest' | 'unreadFirst')}
            style={{
              padding: '4px 10px',
              borderRadius: '8px',
              border: '1px solid var(--border)',
              background: 'var(--surface)',
              color: 'var(--text)',
              fontSize: '13px',
            }}
          >
            <option value="newest">جدیدترین‌ها</option>
            <option value="oldest">قدیمی‌ترین‌ها</option>
            <option value="unreadFirst">ابتدا خوانده‌نشده‌ها</option>
          </select>
        </div>
      </div>

      {notificationsQuery.isLoading ? <LoadingState /> : null}
      {notificationsQuery.isError ? (
        <ErrorState
          error={notificationsQuery.error}
          title="خطا در دریافت اعلانات"
          onRetry={() => void notificationsQuery.refetch()}
        />
      ) : null}

      {notificationsQuery.data && items.length === 0 ? (
        <EmptyState
          title="هیچ اعلانی یافت نشد"
          body={
            filterStatus === 'unread'
              ? 'پیام خوانده‌نشده‌ای در سیستم وجود ندارد.'
              : 'هنوز هیچ درخواست یا اعلانی در سیستم ثبت نشده است.'
          }
        />
      ) : null}

      {items.length > 0 && (
        <div style={{ display: 'flex', flexDirection: 'column', gap: '0.75rem' }}>
          {/* Header row with select all */}
          <div
            style={{
              display: 'flex',
              alignItems: 'center',
              padding: '0.5rem 1rem',
              gap: '0.75rem',
              fontSize: '13px',
              color: 'var(--text-muted)',
            }}
          >
            <input
              type="checkbox"
              id="select-all"
              checked={items.length > 0 && selectedIds.length === items.length}
              onChange={(e) => handleSelectAll(e.target.checked)}
              style={{ cursor: 'pointer', width: 16, height: 16 }}
            />
            <label htmlFor="select-all" style={{ cursor: 'pointer' }}>
              انتخاب همه پیام‌ها ({items.length})
            </label>
          </div>

          {/* Notification Cards */}
          {items.map((notification) => {
            const isSelected = selectedIds.includes(notification.id);
            const isUnread = !notification.isRead;

            return (
              <div
                key={notification.id}
                style={{
                  display: 'flex',
                  alignItems: 'flex-start',
                  gap: '1rem',
                  padding: '1rem',
                  borderRadius: '12px',
                  border: isUnread
                    ? '1px solid var(--accent)'
                    : '1px solid var(--border)',
                  background: isUnread
                    ? 'color-mix(in srgb, var(--accent) 7%, var(--surface))'
                    : 'var(--surface)',
                  transition: 'background 0.15s ease, border-color 0.15s ease',
                  position: 'relative',
                }}
              >
                {/* Checkbox */}
                <input
                  type="checkbox"
                  checked={isSelected}
                  onChange={() => toggleSelectOne(notification.id)}
                  style={{
                    cursor: 'pointer',
                    marginTop: '4px',
                    width: 17,
                    height: 17,
                  }}
                  aria-label="انتخاب پیام"
                />

                {/* Icon */}
                <div style={{ marginTop: '2px', flexShrink: 0 }}>
                  {notification.type === 'PasswordReset' ? (
                    <ThreeDIcon type="shield" size={36} />
                  ) : (
                    <ThreeDIcon type="notes" size={36} />
                  )}
                </div>

                {/* Content */}
                <div style={{ flex: 1, minWidth: 0 }}>
                  <div
                    style={{
                      display: 'flex',
                      alignItems: 'center',
                      gap: '0.5rem',
                      flexWrap: 'wrap',
                      marginBottom: '0.25rem',
                    }}
                  >
                    <h3
                      style={{
                        margin: 0,
                        fontSize: '15px',
                        fontWeight: 700,
                        color: 'var(--text)',
                      }}
                    >
                      {notification.title}
                    </h3>
                    {isUnread ? (
                      <Badge tone="danger">جدید / خوانده‌نشده</Badge>
                    ) : (
                      <Badge tone="neutral">خوانده‌شده</Badge>
                    )}
                    <span
                      style={{
                        fontSize: '12px',
                        color: 'var(--text-muted)',
                        marginInlineStart: 'auto',
                      }}
                    >
                      {formatDate(notification.createdAt)}
                    </span>
                  </div>

                  <p
                    style={{
                      margin: '0.35rem 0 0.65rem',
                      fontSize: '13.5px',
                      color: 'var(--text-muted)',
                      lineHeight: 1.6,
                    }}
                  >
                    {notification.message}
                  </p>

                  {/* Actions Bar */}
                  <div
                    style={{
                      display: 'flex',
                      alignItems: 'center',
                      gap: '0.6rem',
                      flexWrap: 'wrap',
                      marginTop: '0.5rem',
                    }}
                  >
                    {/* Password reset action button */}
                    {notification.type === 'PasswordReset' && (
                      <Button
                        variant="primary"
                        icon="shield"
                        onClick={() => handleOpenResetPassword(notification)}
                        style={{ fontSize: '12.5px', padding: '4px 12px' }}
                      >
                        تغییر رمز عبور کاربر
                      </Button>
                    )}

                    {isUnread && (
                      <Button
                        variant="secondary"
                        icon="check"
                        loading={markReadMutation.isPending}
                        onClick={() => markReadMutation.mutate(notification.id)}
                        style={{ fontSize: '12px', padding: '4px 10px' }}
                      >
                        علامت به عنوان خوانده‌شده
                      </Button>
                    )}

                    <Button
                      variant="ghost"
                      icon="trash"
                      loading={deleteMutation.isPending}
                      onClick={() => {
                        if (confirm('آیا از حذف این اعلان مطمئن هستید؟')) {
                          deleteMutation.mutate(notification.id);
                        }
                      }}
                      style={{ fontSize: '12px', padding: '4px 10px', color: 'var(--danger)' }}
                    >
                      حذف
                    </Button>
                  </div>
                </div>
              </div>
            );
          })}
        </div>
      )}

      {/* Reset Password Modal */}
      {resetTarget && (
        <Modal
          open={!!resetTarget}
          title={`تغییر رمز عبور کاربر: ${resetTarget.email}`}
          onClose={() => setResetTarget(null)}
          footer={
            <>
              <Button onClick={() => setResetTarget(null)}>انصراف</Button>
              <Button
                variant="danger"
                loading={resetPasswordMutation.isPending}
                disabled={newPassword.length < 12}
                onClick={() =>
                  resetPasswordMutation.mutate({
                    userId: resetTarget.userId,
                    password: newPassword,
                  })
                }
              >
                تغییر و ذخیره رمز عبور
              </Button>
            </>
          }
        >
          <div style={{ display: 'flex', flexDirection: 'column', gap: '1rem' }}>
            <p style={{ margin: 0, fontSize: '13px', color: 'var(--text-muted)', lineHeight: 1.6 }}>
              این درخواست توسط کاربر در صفحه ورود وب‌اپلیکیشن ثبت شده است. پس از تغییر رمز عبور، تمام نشست‌های فعال این کاربر خاتمه یافته و کاربر می‌تواند با رمز جدید وارد شود.
            </p>

            {resetError && (
              <div className="notice notice-error" role="alert">
                {resetError}
              </div>
            )}

            <Field
              label="رمز عبور جدید (حداقل ۱۲ کاراکتر)"
              htmlFor="notif-new-password"
            >
              <div style={{ display: 'flex', gap: '0.5rem' }}>
                <Input
                  id="notif-new-password"
                  type="text"
                  placeholder="رمز عبور جدید..."
                  value={newPassword}
                  onChange={(e) => setNewPassword(e.target.value)}
                  autoFocus
                />
                <Button
                  type="button"
                  variant="secondary"
                  onClick={generateRandomPassword}
                  style={{ whiteSpace: 'nowrap' }}
                >
                  تولید رمز قوی
                </Button>
              </div>
            </Field>

            {newPassword.length > 0 && newPassword.length < 12 && (
              <p style={{ margin: 0, fontSize: '12px', color: 'var(--danger)' }}>
                طول رمز عبور باید حداقل ۱۲ کاراکتر باشد.
              </p>
            )}
          </div>
        </Modal>
      )}
    </div>
  );
}
