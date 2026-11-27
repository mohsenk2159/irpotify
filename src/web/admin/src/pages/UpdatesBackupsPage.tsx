import { useRef, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, downloadBackupArchive, restoreBackupArchive } from '../api/endpoints';
import { useUi } from '../ui/UiProvider';
import { Modal, useToast } from '../ui/Modal';
import { Badge, Button, Card, DefinitionList, LoadingState, PageHeader } from '../ui/primitives';
import { errorMessage } from '../ui/helpers';

export function UpdatesBackupsPage() {
  const { formatDate, formatBytes } = useUi();
  const { notify } = useToast();
  const queryClient = useQueryClient();

  const [downloadingBackup, setDownloadingBackup] = useState(false);
  const [updateModalOpen, setUpdateModalOpen] = useState(false);
  const [preUpdateBackupDone, setPreUpdateBackupDone] = useState(false);
  const [preUpdateBackupFile, setPreUpdateBackupFile] = useState<string | null>(null);
  const [updateResultMsg, setUpdateResultMsg] = useState<{ message: string; commandHint: string | null } | null>(null);

  const [restoreProgress, setRestoreProgress] = useState<number | null>(null);
  const [restoring, setRestoring] = useState(false);
  const [restoreResult, setRestoreResult] = useState<string | null>(null);
  const [restoreError, setRestoreError] = useState<string | null>(null);
  const fileInputRef = useRef<HTMLInputElement | null>(null);

  const updateQuery = useQuery({
    queryKey: ['system', 'update-status'],
    queryFn: () => api.system.updateStatus(),
  });

  const backupsQuery = useQuery({
    queryKey: ['admin', 'backups'],
    queryFn: () => api.dashboard.backups(),
  });

  const checkMutation = useMutation({
    mutationFn: () => api.system.checkUpdate(),
    onSuccess: (data) => {
      void queryClient.setQueryData(['system', 'update-status'], data);
      void queryClient.invalidateQueries({ queryKey: ['notifications', 'unread-count'] });
      if (data.updateAvailable) {
        notify('نسخه جدیدی در مخزن گیتهاب موجود است!', 'info');
      } else {
        notify('سیستم در حال حاضر روی آخرین نسخه است.', 'success');
      }
    },
    onError: (err) => notify(errorMessage(err, 'خطا در بررسی گیتهاب'), 'error'),
  });

  const applyUpdateMutation = useMutation({
    mutationFn: () => api.system.applyUpdate(),
    onSuccess: (res) => {
      setUpdateResultMsg({ message: res.message, commandHint: res.commandHint });
      void queryClient.invalidateQueries({ queryKey: ['system', 'update-status'] });
      if (res.applied) {
        notify('آپدیت سورس سرور با موفقیت انجام شد.', 'success');
      }
    },
    onError: (err) => notify(errorMessage(err, 'خطا در اجرای آپدیت'), 'error'),
  });

  const handleDownloadBackup = async (forPreUpdate = false) => {
    setDownloadingBackup(true);
    try {
      const fileName = await downloadBackupArchive();
      notify(`فایل بکاپ کامل (${fileName}) با موفقیت ذخیره شد.`, 'success');
      if (forPreUpdate) {
        setPreUpdateBackupDone(true);
        setPreUpdateBackupFile(fileName);
      }
      void queryClient.invalidateQueries({ queryKey: ['admin', 'backups'] });
    } catch (err) {
      notify(errorMessage(err, 'خطا در دریافت فایل بکاپ'), 'error');
    } finally {
      setDownloadingBackup(false);
    }
  };

  const handleRestoreFile = async (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    e.target.value = '';
    if (!file) return;

    setRestoring(true);
    setRestoreProgress(0);
    setRestoreError(null);
    setRestoreResult(null);

    try {
      const res = await restoreBackupArchive(file, (pct) => setRestoreProgress(pct));
      setRestoreResult(
        `${res.message} (${res.usersRestored} کاربر، ${res.artistsRestored} هنرمند، ${res.albumsRestored} آلبوم، ${res.tracksRestored} آهنگ، ${res.playlistsRestored} پلی‌لیست، ${res.filesRestored} فایل)`,
      );
      notify('بازیابی کامل اطلاعات و فایل‌ها با موفقیت انجام شد.', 'success');
      void queryClient.invalidateQueries();
    } catch (err) {
      const msg = errorMessage(err, 'خطا در بازیابی فایل بکاپ');
      setRestoreError(msg);
      notify(msg, 'error');
    } finally {
      setRestoring(false);
      setRestoreProgress(null);
    }
  };

  const status = updateQuery.data;

  return (
    <div className="stack">
      <PageHeader
        title="آپدیت سیستم و پشتیبان‌گیری سازمانی"
        subtitle="بررسی خودکار روزانه گیتهاب، آپدیت ایمن همراه با بکاپ اجباری، و بازیابی کامل سرور"
        actions={
          <Button
            icon="refresh"
            loading={checkMutation.isPending}
            onClick={() => checkMutation.mutate()}
          >
            بررسی آپدیت از گیتهاب
          </Button>
        }
      />

      {/* 1. GitHub Auto-Update Card */}
      <Card
        title="به‌روزرسانی از مخزن گیتهاب"
        actions={
          status?.updateAvailable ? (
            <Badge tone="warning">آپدیت جدید موجود است</Badge>
          ) : (
            <Badge tone="success">به‌روز</Badge>
          )
        }
      >
        {updateQuery.isLoading ? (
          <LoadingState label="در حال دریافت وضعیت نسخه..." />
        ) : (
          <div className="stack">
            <DefinitionList
              items={[
                { label: 'نسخه نرم‌افزار', value: status?.currentVersion ?? '1.0.0' },
                {
                  label: 'کامیت فعلی سرور',
                  value: <span className="mono">{status?.currentCommitSha?.slice(0, 10) || '—'}</span>,
                },
                {
                  label: 'آخرین کامیت گیتهاب',
                  value: <span className="mono">{status?.latestCommitSha?.slice(0, 10) || '—'}</span>,
                },
                { label: 'توضیحات آخرین تغییر', value: status?.latestCommitMessage || '—' },
                {
                  label: 'آخرین بررسی خودکار (هر ۲۴ ساعت)',
                  value: status?.lastCheckedAt ? formatDate(status.lastCheckedAt) : 'هنوز بررسی نشده',
                },
              ]}
            />

            {status?.lastError ? (
              <div className="notice notice-error">{status.lastError}</div>
            ) : null}

            <div className="row" style={{ gap: 10 }}>
              <Button
                variant="primary"
                icon="refresh"
                onClick={() => {
                  setPreUpdateBackupDone(false);
                  setPreUpdateBackupFile(null);
                  setUpdateResultMsg(null);
                  setUpdateModalOpen(true);
                }}
              >
                شروع آپدیت سیستم
              </Button>
            </div>
          </div>
        )}
      </Card>

      {/* 2. Enterprise Full Backup & Restore */}
      <div className="grid two">
        <Card title="پشتیبان‌گیری کامل (Full Backup)">
          <div className="stack">
            <p className="muted small">
              تمامی کاربران (با رمز عبور)، آهنگ‌ها، آلبوم‌ها، هنرمندان، لیست‌های پخش، کاورها، تنظیمات و
              پیکربندی ربات تلگرام را همراه با چک‌سام SHA-256 در یک فایل فشرده <code>.zip</code> دریافت کنید تا
              در سرور جدید عیناً بازیابی شود.
            </p>
            <div>
              <Button
                variant="primary"
                icon="upload"
                loading={downloadingBackup}
                onClick={() => void handleDownloadBackup(false)}
              >
                دریافت و ذخیره فایل بکاپ کامل (.zip)
              </Button>
            </div>
          </div>
        </Card>

        <Card title="بازیابی از روی فایل بکاپ (Disaster Recovery)">
          <div className="stack">
            <p className="muted small">
              فایل بکاپ <code>.zip</code> را انتخاب کنید. پیش از اعمال تغییرات، یکپارچگی SHA-256 تمام فایل‌ها و
              مسیرها بررسی شده و اطلاعات در یک تراکنش اتمیک وارد سرور می‌شوند.
            </p>

            {restoreResult ? <div className="notice notice-success">{restoreResult}</div> : null}
            {restoreError ? <div className="notice notice-error">{restoreError}</div> : null}

            {restoreProgress !== null ? (
              <div className="small muted">در حال آپلود و بررسی امنیتی بکاپ: {restoreProgress}%</div>
            ) : null}

            <div>
              <Button
                icon="restore"
                loading={restoring}
                onClick={() => fileInputRef.current?.click()}
              >
                انتخاب فایل بکاپ (.zip) و بازیابی کامل
              </Button>
              <input
                ref={fileInputRef}
                type="file"
                accept=".zip"
                style={{ display: 'none' }}
                onChange={(e) => void handleRestoreFile(e)}
              />
            </div>
          </div>
        </Card>
      </div>

      {/* 3. Backup & Restore History */}
      <Card title="تاریخچه بکاپ و بازیابی">
        {backupsQuery.isLoading ? (
          <LoadingState label="در حال بارگذاری تاریخچه..." />
        ) : (backupsQuery.data ?? []).length === 0 ? (
          <p className="muted small">هنوز هیچ بکاپ یا بازیابی ثبت نشده است.</p>
        ) : (
          <div className="table-wrap">
            <table className="table">
              <thead>
                <tr>
                  <th>نام فایل</th>
                  <th>وضعیت</th>
                  <th>حجم</th>
                  <th>تاریخ</th>
                </tr>
              </thead>
              <tbody>
                {(backupsQuery.data ?? []).map((b) => (
                  <tr key={b.id}>
                    <td className="mono">{b.fileName}</td>
                    <td>
                      <Badge tone={b.status === 'Completed' || b.status === 'Restored' ? 'success' : 'neutral'}>
                        {b.status}
                      </Badge>
                    </td>
                    <td>{formatBytes(b.sizeBytes)}</td>
                    <td>{formatDate(b.startedAt)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Card>

      {/* Mandatory Pre-Update Backup Modal */}
      <Modal
        open={updateModalOpen}
        title="آپدیت ایمن سیستم (پشتیبان‌گیری اجباری)"
        onClose={() => setUpdateModalOpen(false)}
        footer={
          <>
            <Button onClick={() => setUpdateModalOpen(false)}>بستن</Button>
            <Button
              variant="primary"
              disabled={!preUpdateBackupDone}
              loading={applyUpdateMutation.isPending}
              onClick={() => applyUpdateMutation.mutate()}
            >
              تایید و شروع آپدیت از گیتهاب
            </Button>
          </>
        }
      >
        <div className="stack">
          <div className="notice notice-info">
            <strong>مرحله ۱ (الزامی):</strong> قبل از دریافت نسخه جدید، حتماً یک نسخه بکاپ کامل از اطلاعات
            (کاربران، آهنگ‌ها، آلبوم‌ها و تنظیمات) روی سیستم خود ذخیره کنید.
          </div>

          <div>
            <Button
              variant={preUpdateBackupDone ? 'ghost' : 'primary'}
              loading={downloadingBackup}
              onClick={() => void handleDownloadBackup(true)}
            >
              {preUpdateBackupDone
                ? `✓ بکاپ ذخیره شد (${preUpdateBackupFile})`
                : '۱. دانلود و ذخیره بکاپ کامل (.zip)'}
            </Button>
          </div>

          {updateResultMsg ? (
            <div className="notice notice-success">
              <p style={{ margin: 0 }}>{updateResultMsg.message}</p>
              {updateResultMsg.commandHint ? (
                <pre className="mono" style={{ marginTop: 8, padding: 8, background: 'rgba(0,0,0,0.25)', borderRadius: 6 }}>
                  {updateResultMsg.commandHint}
                </pre>
              ) : null}
            </div>
          ) : null}
        </div>
      </Modal>
    </div>
  );
}
