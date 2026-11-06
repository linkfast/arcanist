import { CdkMenu, CdkMenuItem, CdkMenuTrigger } from '@angular/cdk/menu';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideBan,
  lucideCircleCheck,
  lucideEllipsis,
  lucideKeyRound,
  lucideShieldOff,
  lucidePencil,
  lucidePlus,
  lucideTrash2,
} from '@ng-icons/lucide';
import { BrnAlertDialogContent } from '@spartan-ng/brain/alert-dialog';
import { BrnDialogContent } from '@spartan-ng/brain/dialog';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmAlertDialogImports } from '@spartan-ng/helm/alert-dialog';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCheckboxImports } from '@spartan-ng/helm/checkbox';
import { HlmDialogImports } from '@spartan-ng/helm/dialog';
import { HlmFieldImports } from '@spartan-ng/helm/field';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmNativeSelectImports } from '@spartan-ng/helm/native-select';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { HlmTableImports } from '@spartan-ng/helm/table';
import { ApiError, Permission, UserRole } from '../../core/api/rest';
import { AuthService } from '../../core/auth/auth.service';
import { Gql } from '../../core/graphql/gql.service';
import { I18n, TranslatePipe } from '../../core/i18n/i18n';
import type {
  UserFieldsFragment,
  UsersQuery as UsersResult,
} from '../../graphql/generated/graphql';
import {
  CreateUserMutation,
  DeleteUserMutation,
  ResetUserPasswordMutation,
  ResetUserSecondFactorMutation,
  SetUserLibrariesMutation,
  UpdateUserMutation,
  UsersQuery,
} from '../../graphql/operations';
import { RelativeTimePipe, searchKey } from '../../shared/format';
import { AccessLevels, LibraryAccessEditor, grantsOf, levelsOf } from './library-access-editor';

type User = UserFieldsFragment;
type Library = UsersResult['libraries'][number];
type UserFilter = 'all' | 'pending' | 'admins' | 'disabled';
type FormTab = 'account' | 'permissions' | 'libraries';

/** The permissions by what they are about, as the user's page shows them. */
const permissionGroups: { id: string; permissions: Permission[] }[] = [
  { id: 'music', permissions: ['DOWNLOAD', 'UPLOAD', 'EDIT_METADATA', 'DELETE_TRACKS'] },
  { id: 'sharing', permissions: ['SHARE_LINKS', 'INVITE', 'PLAY_ON_SERVER'] },
  {
    id: 'server',
    permissions: [
      'MANAGE_LIBRARIES',
      'RUN_SCANS',
      'MANAGE_USERS',
      'MANAGE_SETTINGS',
      'VIEW_ALL_STATS',
      'VIEW_ALL_WISHES',
    ],
  },
];

interface UserForm {
  id: string | null;
  userName: string;
  displayName: string;
  password: string;
  role: UserRole;
  permissions: Permission[];
  isDisabled: boolean;
  /** Asked for with an invitation: saving the form lets it in. */
  pendingApproval: boolean;
  /** What they may do in each library. */
  levels: AccessLevels;
}

/**
 * The server's accounts in one list. Each opens one page with all of it (the account, what it may do, its libraries),
 * the same one that creates an account; the rest of what can be done to it is in its menu.
 */
@Component({
  selector: 'app-users-page',
  imports: [
    FormsModule,
    NgIcon,
    CdkMenu,
    CdkMenuItem,
    CdkMenuTrigger,
    BrnDialogContent,
    BrnAlertDialogContent,
    HlmButtonImports,
    HlmBadgeImports,
    HlmTableImports,
    HlmDialogImports,
    HlmAlertDialogImports,
    HlmFieldImports,
    HlmInputImports,
    HlmCheckboxImports,
    HlmNativeSelectImports,
    HlmSpinnerImports,
    TranslatePipe,
    RelativeTimePipe,
    LibraryAccessEditor,
  ],
  providers: [
    provideIcons({
      lucidePlus,
      lucidePencil,
      lucideTrash2,
      lucideKeyRound,
      lucideShieldOff,
      lucideEllipsis,
      lucideBan,
      lucideCircleCheck,
    }),
  ],
  templateUrl: './users.page.html',
})
export class UsersPage implements OnInit {
  protected readonly auth = inject(AuthService);
  private readonly gql = inject(Gql);
  private readonly i18n = inject(I18n);

  protected readonly menuItem =
    'flex w-full items-center gap-2 rounded-sm px-2 py-1.5 text-left outline-none hover:bg-accent focus:bg-accent';

  protected readonly users = signal<User[] | null>(null);
  protected readonly libraries = signal<Library[]>([]);
  protected readonly busy = signal(false);

  protected readonly form = signal<UserForm | null>(null);
  protected readonly formTabs: FormTab[] = ['account', 'permissions', 'libraries'];
  protected readonly formTab = signal<FormTab>('account');
  /** An administrator has everything: only the account has anything to choose. */
  protected readonly visibleTab = computed<FormTab>(() =>
    this.form()?.role === 'ADMIN' ? 'account' : this.formTab(),
  );
  protected readonly formError = signal<string | null>(null);
  /** Saving was tried: what is missing says so. */
  protected readonly submitted = signal(false);
  protected readonly passwordVisible = signal(false);

  protected readonly resetting = signal<User | null>(null);
  protected newPassword = '';
  protected readonly deleting = signal<User | null>(null);
  protected readonly toggling = signal<User | null>(null);
  protected readonly resettingFactor = signal<User | null>(null);

  /** Permissions the current user may grant: all for administrators, their own otherwise. */
  protected readonly permissionGroups = computed(() =>
    permissionGroups
      .map((group) => ({
        id: group.id,
        permissions: group.permissions.filter((p) => this.auth.can(p)),
      }))
      .filter((group) => group.permissions.length > 0),
  );
  /** Every library but users' personal ones: each user listens to it, adds to it, or (if restricted) neither. */
  protected readonly accessLibraries = computed(() => this.libraries().filter((l) => !l.personal));

  protected readonly filters: UserFilter[] = ['all', 'pending', 'admins', 'disabled'];
  protected readonly filter = signal<UserFilter>('all');
  protected readonly pendingCount = computed(
    () => (this.users() ?? []).filter((user) => user.pendingApproval).length,
  );
  protected readonly userQuery = signal('');
  protected readonly filteredUsers = computed(() => {
    const words = searchKey(this.userQuery()).trim().split(/\s+/).filter(Boolean);
    const filter = this.filter();
    return (this.users() ?? []).filter((user) => {
      if (filter === 'pending' && !user.pendingApproval) return false;
      if (filter === 'admins' && user.role !== 'ADMIN') return false;
      if (filter === 'disabled' && !user.isDisabled) return false;
      const text = searchKey(`${user.userName} ${user.displayName ?? ''}`);
      return words.every((word) => text.includes(word));
    });
  });

  /** «5 of 8 libraries · 2 listen only», or that an administrator reaches them all. */
  protected accessText(user: User): string {
    if (user.role === 'ADMIN') return this.i18n.t('users.adminAccess');
    const levels = Object.values(
      levelsOf(this.accessLibraries(), user.libraryIds, user.readOnlyLibraryIds),
    );
    const summary = {
      count: levels.filter((l) => l !== 'none').length,
      total: levels.length,
      listen: levels.filter((l) => l === 'listen').length,
    };
    const text = this.i18n.t('users.accessSummary', summary);
    return summary.listen > 0
      ? `${text} · ${this.i18n.t('users.accessListenOnly', summary)}`
      : text;
  }

  protected permissionText(user: User): string {
    if (user.role === 'ADMIN') return this.i18n.t('users.allPermissions');
    return user.permissions.length === 0
      ? this.i18n.t('users.listenOnly')
      : this.i18n.t('users.permissionCount', { count: user.permissions.length });
  }

  protected permissionNames(user: User): string {
    return user.permissions.map((p) => this.i18n.t(`permissions.${p}`)).join(', ');
  }

  async ngOnInit(): Promise<void> {
    await this.load();
  }

  protected canEdit(user: User): boolean {
    return this.auth.isAdmin() || user.role !== 'ADMIN';
  }

  private openForm(form: UserForm): void {
    this.formError.set(null);
    this.submitted.set(false);
    this.passwordVisible.set(false);
    this.formTab.set('account');
    this.form.set(form);
  }

  protected openCreate(): void {
    this.openForm({
      id: null,
      userName: '',
      displayName: '',
      password: '',
      role: 'USER',
      permissions: [],
      isDisabled: false,
      pendingApproval: false,
      levels: levelsOf(this.accessLibraries(), [], []),
    });
  }

  protected openEdit(user: User): void {
    this.openForm({
      id: user.id,
      userName: user.userName,
      displayName: user.displayName ?? '',
      password: '',
      role: user.role,
      permissions: [...user.permissions],
      // Opening one that waits for approval is approving it: saving turns it on.
      isDisabled: user.pendingApproval ? false : user.isDisabled,
      pendingApproval: user.pendingApproval,
      levels: levelsOf(this.accessLibraries(), user.libraryIds, user.readOnlyLibraryIds),
    });
  }

  protected updateForm(change: Partial<UserForm>): void {
    this.form.update((form) => (form ? { ...form, ...change } : form));
  }

  protected togglePermission(permission: Permission, granted: boolean): void {
    this.form.update((form) =>
      form
        ? {
            ...form,
            permissions: granted
              ? [...form.permissions, permission]
              : form.permissions.filter((p) => p !== permission),
          }
        : form,
    );
  }

  /** A password to hand over, shown so it can be copied. */
  protected generatePassword(): void {
    const alphabet = 'abcdefghijkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789';
    const limit = 256 - (256 % alphabet.length);
    let password = '';
    while (password.length < 16) {
      for (const byte of crypto.getRandomValues(new Uint8Array(32))) {
        if (byte < limit && password.length < 16) password += alphabet[byte % alphabet.length];
      }
    }
    this.updateForm({ password });
    this.passwordVisible.set(true);
  }

  protected async copy(text: string): Promise<void> {
    await navigator.clipboard.writeText(text);
    toast.success(this.i18n.t('common.copied'));
  }

  protected async saveForm(): Promise<void> {
    const form = this.form();
    if (!form) return;

    this.submitted.set(true);
    if (!form.userName.trim() || (!form.id && form.password.length < 8)) {
      // What is missing is on the account's tab.
      this.formTab.set('account');
      return;
    }

    this.busy.set(true);
    this.formError.set(null);
    try {
      const permissions = form.role === 'ADMIN' ? [] : form.permissions;
      if (form.id) {
        await this.gql.mutate(UpdateUserMutation, {
          id: form.id,
          input: {
            displayName: form.displayName.trim() || null,
            role: form.role,
            permissions,
            isDisabled: form.isDisabled,
          },
        });
        if (form.role !== 'ADMIN') {
          await this.gql.mutate(SetUserLibrariesMutation, {
            id: form.id,
            input: grantsOf(this.accessLibraries(), form.levels),
          });
        }
      } else {
        await this.gql.mutate(CreateUserMutation, {
          input: {
            userName: form.userName.trim(),
            password: form.password,
            displayName: form.displayName.trim() || null,
            role: form.role,
            permissions,
            ...(form.role === 'ADMIN'
              ? { libraryIds: [], readOnlyLibraryIds: [] }
              : grantsOf(this.accessLibraries(), form.levels)),
          },
        });
      }
      this.form.set(null);
      toast.success(
        form.pendingApproval && !form.isDisabled
          ? this.i18n.t('users.approved', { name: form.userName })
          : this.i18n.t('users.saved'),
      );
      await this.load();
    } catch (error) {
      this.formError.set(this.describe(ApiError.from(error)));
    } finally {
      this.busy.set(false);
    }
  }

  protected async confirmReset(): Promise<void> {
    const user = this.resetting();
    if (!user) return;

    await this.run(async () => {
      await this.gql.mutate(ResetUserPasswordMutation, {
        id: user.id,
        input: { newPassword: this.newPassword },
      });
      this.resetting.set(null);
      this.newPassword = '';
      toast.success(this.i18n.t('users.passwordReset', { name: user.userName }));
    });
  }

  protected async confirmResetFactor(user: User): Promise<void> {
    this.resettingFactor.set(null);
    await this.run(async () => {
      await this.gql.mutate(ResetUserSecondFactorMutation, { userId: user.id });
      toast.success(this.i18n.t('users.secondFactorReset', { name: user.userName }));
      await this.load();
    });
  }

  protected async confirmToggle(user: User): Promise<void> {
    this.toggling.set(null);
    await this.run(async () => {
      await this.gql.mutate(UpdateUserMutation, {
        id: user.id,
        input: {
          displayName: user.displayName ?? null,
          role: user.role,
          permissions: user.permissions,
          isDisabled: !user.isDisabled,
        },
      });
      toast.success(
        this.i18n.t(user.isDisabled ? 'users.enabled' : 'users.disabledDone', {
          name: user.userName,
        }),
      );
      await this.load();
    });
  }

  protected async confirmDelete(): Promise<void> {
    const user = this.deleting();
    this.deleting.set(null);
    if (!user) return;

    await this.run(async () => {
      await this.gql.mutate(DeleteUserMutation, { id: user.id });
      toast.success(this.i18n.t('users.deleted', { name: user.userName }));
      await this.load();
    });
  }

  private async load(): Promise<void> {
    const result = await this.gql.query(UsersQuery);
    this.users.set(result.users);
    this.libraries.set(result.libraries);
  }

  private async run(action: () => Promise<unknown>): Promise<void> {
    this.busy.set(true);
    try {
      await action();
    } catch (error) {
      toast.error(this.describe(ApiError.from(error)));
    } finally {
      this.busy.set(false);
    }
  }

  private describe(error: ApiError): string {
    const fieldCode = Object.values(error.fields)[0]?.[0];
    return this.i18n.error(fieldCode ?? error.code);
  }
}
