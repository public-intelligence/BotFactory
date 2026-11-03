# @public-intelligence/ui-core Component Library Reference

When building Angular frontend features in this project, use the `@public-intelligence/ui-core` library (v0.0.16). All components are standalone and use Tailwind CSS.

The library's own reference is `UI-Core/COMPONENTES-UI-CORE.md` in the UICORE repository. Update this file when UI-Core changes version.

## Layout

- **AdminLayoutComponent** (`app-admin-layout`): Main layout shell. Inputs: `branding` (AppBranding), `menuItems` (SidebarMenuItem[]), `userProfile` (UserProfile), `profileMenuItems` (ProfileMenuItem[]).
- **HeaderComponent** (`app-header`): Top header with breadcrumbs and profile dropdown.
- **SidebarComponent** (`app-sidebar`): Collapsible sidebar nav.
- **LayoutService**: Injectable service managing layout state via signals — `isSidebarCollapsed`, `pageTitle`, `breadcrumbs`, `branding`, `userProfile`, `menuItems`, `profileMenuItems`. Methods: `toggleSidebar()`, `setPageTitle(title)`, `emitProfileMenuAction(action)`. Auto-generates breadcrumbs from router state.

## Data Display

- **DataTableComponent** (`app-data-table`): Inputs: `data` (PagedData), `columns` (ColumnDef[]), `loading`, `tableCaption`. Output: `pageChange`.
- **PaginatedTableComponent** (`app-paginated-table`): Inputs: `data` (PaginatedResult<T>), `columns` (PaginatedColumnDef[]), `loading`. Output: `pageChange`.
- **TreeViewComponent** (`app-tree-view`): Inputs: `nodes` (TreeNode[]), `selectedId`, `ariaLabel`. Output: `nodeSelected`. Keyboard navigation built in.
- **StatusPillComponent** (`app-status-pill`): Inputs: `status`, `type` ('publication'|'access'|'validity').

## Forms & Inputs

- **DropdownSearchComponent** (`app-dropdown-search`): Inputs: `options` (DropdownOption[]), `value`, `placeholder`, `searchPlaceholder`. Output: `valueChange`.
- **DropdownMultiSelectComponent** (`app-dropdown-multi-select`): Inputs: `options` (DropdownOption[]), `value` (string[]), `placeholder`, `closeOnSelect`, `showSearch`. Output: `valueChange`.
- **FilterBarComponent** (`app-filter-bar`): Inputs: `searchLabel`, `searchPlaceholder`, `searchQuery`, `showDeletedToggle`, `includeDeleted`. Outputs: `searchQueryChange`, `search`, `includeDeletedChange`. Supports `[advanced-filters]` content projection.

## Dialogs & Modals

- **ConfirmModalComponent** (`app-confirm-modal`): Inputs: `isOpen`, `title`, `message`, `confirmText`, `cancelText`, `type` ('danger'|'primary'|'warning'), `isProcessing`. Outputs: `confirm`, `cancel`.
- **ConfirmationDialogComponent** (`app-confirmation-dialog`): More feature-rich dialog with focus trap. Inputs: `title`, `message`, `confirmText`, `cancelText`, `type` ('danger'|'info'), `isOpen`, `error`, `isProcessing`. Outputs: `onConfirm`, `onCancel`.

## Banners & Notifications

- **AlertBannerComponent** (`app-alert-banner`): Inputs: `type` ('success'|'info'|'warning'|'error'), `title`, `message`.
- **NotificationBannerComponent** (`app-notification-banner`): Inputs: `type` ('success'|'error'|'info'), `title`, `message`, `visible`. Supports content projection.
- **ImmutableBannerComponent** (`app-immutable-banner`): Input: `status`. Output: `cloneClicked`.
- **ToastService**: Injectable. Methods: `showSuccess(msg)`, `showError(msg)`, `showInfo(msg)`, `remove(id)`. Signal: `toasts`.

## Page Shells

- **PageShellComponent** (`app-page-shell`): Inputs: `title`, `description?`. Content projection: `[breadcrumbs]`, `[status]`, `[subtitle]`, `[actions]`, `[filters]`, default.
- **DetailPageShellComponent** (`app-detail-page-shell`): Input: `title`. Content projection: `[subtitle]`, `[actions]`, `[alerts]`, default.

## Other

- **ActionMenuComponent** (`app-action-menu`): Inputs: `items`, `ariaLabel`. Output: `action`.
- **CredentialsLoaderComponent** (`app-credentials-loader`): Full-screen overlay with a centered spinner, for OAuth or other credential checks. Inputs: `message`, `subtitle`, `spinnerColor`.
- **ErrorSummaryComponent** (`app-error-summary`): Input: `errors` (ErrorSummaryItem[]).
- **ValidationResultsPanelComponent** (`app-validation-results-panel`): Inputs: `violations`, `passed`. Output: `jump`.

## Key Interfaces

```typescript
interface SidebarMenuItem { label: string; route?: string; url?: string; iconPath?: string; target?: '_blank'|'_self'; exactMatch?: boolean; }
interface AppBranding { appName: string; moduleName: string; logoUrl: string; }
interface UserProfile { name: string; role: string; avatarUrl?: string; }
interface ProfileMenuItem { label: string; action: string; destructive?: boolean; }
interface DropdownOption { value: string; label: string; disabled?: boolean; }
interface ColumnDef { header: string; field?: string; template?: TemplateRef<any>; width?: string; align?: 'left'|'center'|'right'; }
interface PagedData { items: any[]; totalItems: number; page: number; pageSize: number; totalPages?: number; }
interface PaginatedResult<T> { items: T[]; page: number; pageSize: number; totalItems: number; totalPages: number; }
interface TreeNode { id: string; label: string; children?: TreeNode[]; data?: any; expanded?: boolean; }
interface ErrorSummaryItem { fieldId: string; message: string; }
interface Toast { id: number; message: string; type: 'success'|'error'|'info'; }
```

## Utility

- `toPaginatedResult<T>(apiResponse: any): PaginatedResult<T>` — converts API responses to PaginatedResult format.

## Tailwind

The package ships a `tailwind.preset.js` with design system tokens (colors, fonts, radii via CSS variables like `sdd-color-*`, `sdd-font-*`, `sdd-radius-*`). Import it in your Tailwind config.

## Import

All exports are available from `@public-intelligence/ui-core`:
```typescript
import { AdminLayoutComponent, LayoutService, ToastService, PageShellComponent, ... } from '@public-intelligence/ui-core';
```
