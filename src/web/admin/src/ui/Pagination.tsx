import { useUi } from './UiProvider';
import { Icon } from './icons';

interface PaginationProps {
  page: number;
  pageSize: number;
  total: number;
  onPageChange: (page: number) => void;
  onPageSizeChange?: (pageSize: number) => void;
  pageSizes?: number[];
}

function rangePages(page: number, pages: number): (number | 'gap')[] {
  const wanted = new Set<number>();
  for (const candidate of [1, pages, page - 2, page - 1, page, page + 1, page + 2]) {
    if (candidate >= 1 && candidate <= pages) wanted.add(candidate);
  }
  const sorted = [...wanted].sort((a, b) => a - b);
  const result: (number | 'gap')[] = [];
  let previous = 0;
  for (const value of sorted) {
    if (previous && value - previous > 1) result.push('gap');
    result.push(value);
    previous = value;
  }
  return result;
}

export function Pagination({
  page,
  pageSize,
  total,
  onPageChange,
  onPageSizeChange,
  pageSizes = [25, 50, 100, 200],
}: PaginationProps) {
  const { t, formatNumber } = useUi();
  const pages = Math.max(1, Math.ceil(total / Math.max(1, pageSize)));
  const current = Math.min(Math.max(1, page), pages);
  const from = total === 0 ? 0 : (current - 1) * pageSize + 1;
  const to = Math.min(total, current * pageSize);

  return (
    <nav className="pagination" aria-label={t('a11y.pagination')}>
      <p className="pagination-summary">
        {t('common.showing', {
          from: formatNumber(from),
          to: formatNumber(to),
          total: formatNumber(total),
        })}
      </p>
      <div className="pagination-controls">
        {onPageSizeChange ? (
          <label className="pagination-size">
            <span>{t('common.pageSize')}</span>
            <select
              className="input select select-sm"
              value={pageSize}
              onChange={(event) => onPageSizeChange(Number(event.target.value))}
            >
              {pageSizes.map((size) => (
                <option key={size} value={size}>
                  {formatNumber(size)}
                </option>
              ))}
            </select>
          </label>
        ) : null}
        <div className="pagination-buttons">
          <button
            type="button"
            className="btn btn-secondary btn-icon"
            aria-label={t('a11y.firstPage')}
            disabled={current <= 1}
            onClick={() => onPageChange(1)}
          >
            <span className="pager-double" aria-hidden="true">
              <Icon name="chevron-left" size={16} />
              <Icon name="chevron-left" size={16} />
            </span>
          </button>
          <button
            type="button"
            className="btn btn-secondary btn-icon pager-prev"
            aria-label={t('a11y.previousPage')}
            disabled={current <= 1}
            onClick={() => onPageChange(current - 1)}
          >
            <Icon name="chevron-left" size={18} />
          </button>
          {rangePages(current, pages).map((entry, index) =>
            entry === 'gap' ? (
              <span key={`gap-${index}`} className="pagination-gap" aria-hidden="true">
                …
              </span>
            ) : (
              <button
                key={entry}
                type="button"
                className={`btn btn-page${entry === current ? ' is-current' : ''}`}
                aria-label={t('common.pageOf', { page: formatNumber(entry), pages: formatNumber(pages) })}
                aria-current={entry === current ? 'page' : undefined}
                onClick={() => onPageChange(entry)}
              >
                {formatNumber(entry)}
              </button>
            ),
          )}
          <button
            type="button"
            className="btn btn-secondary btn-icon pager-next"
            aria-label={t('a11y.nextPage')}
            disabled={current >= pages}
            onClick={() => onPageChange(current + 1)}
          >
            <Icon name="chevron-right" size={18} />
          </button>
          <button
            type="button"
            className="btn btn-secondary btn-icon"
            aria-label={t('a11y.lastPage')}
            disabled={current >= pages}
            onClick={() => onPageChange(pages)}
          >
            <span className="pager-double" aria-hidden="true">
              <Icon name="chevron-right" size={16} />
              <Icon name="chevron-right" size={16} />
            </span>
          </button>
        </div>
      </div>
    </nav>
  );
}
