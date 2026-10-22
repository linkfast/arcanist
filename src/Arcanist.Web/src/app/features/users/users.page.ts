import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideKeyRound, lucidePencil, lucidePlus, lucideTrash2 } from '@ng-icons/lucide';
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
import { HlmSwitchImports } from '@spartan-ng/helm/switch';
import { HlmTableImports } from '@spartan-ng/helm/table';
import { HlmTabsImports } from '@spartan-ng/helm/tabs';
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
  SetUserLibrariesMutation,
  UpdateUserMutation,
  UsersQuery,
} from '../../graphql/operations';
import { AccessLevels, LibraryAccessEditor, grantsOf, levelsOf } from './library-access-editor';
import { RelativeTimePipe, searchKey } from '../../shared/format';

type User = UserFieldsFragment;
type Library = UsersResult['libraries'][number];

const allPermissions: Permission[] = [
  'MANAGE_LIBRARIES',
  'MANAGE_USERS',
  'MANAGE_SETTINGS',
  'RUN_SCANS',
  'DOWNLOAD',
  'VIEW_ALL_STATS',
  'UPLOAD',
  'PLAY_ON_SERVER',
  'VIEW_ALL_WISHES',
];

interface UserForm {
  id: string | null;
  userName: string;
  displayName: string;
  password: string;
  role: UserRole;
  permissions: Permission[];
  isDisabled: boolean;
  /** For a new user: what they may do in each library. */
  levels: AccessLevels;
}

@Component({
  selector: 'app-users-page',
  imports: [
    FormsModule,
    NgIcon,
    BrnDialogContent,
    BrnAlertDialogContent,
    HlmButtonImports,
    HlmBadgeImports,
    HlmTableImports,
    HlmTabsImports,
    HlmDialogImports,
    HlmAlertDialogImports,
    HlmFieldImports,
    HlmInputImports,
    HlmCheckboxImports,
    HlmSwitchImports,
    HlmNativeSelectImports,
    HlmSpinnerImports,
    TranslatePipe,
    RelativeTimePipe,
    LibraryAccessEditor,
  ],
  providers: [provideIcons({ lucidePlus, lucidePencil, lucideTrash2, lucideKeyRound })],
  templateUrl: './users.page.html',
})
export class UsersPage implements OnInit {
  protected readonly auth = inject(AuthService);
  private readonly gql = inject(Gql);
  private readonly i18n = inject(I18n);

  protected readonly users = signal<User[] | null>(null);
  protected readonly libraries = signal<Library[]>([]);
  protected readonly busy = signal(false);

  protected readonly form = signal<UserForm | null>(null);
  protected readonly formError = signal<string | null>(null);
  protected readonly resetting = signal<User | null>(null);
  protected newPassword = '';
  protected readonly deleting = signal<User | null>(null);

  /** Permissions the current user may grant: all for administrators, their own otherwise. */
  protected readonly grantable = computed(() => allPermissions.filter((p) => this.auth.can(p)));
  /** Every library but users' personal ones: each user listens to it, adds to it, or (if restricted) neither. */
  protected readonly accessLibraries = computed(() => this.libraries().filter((l) => !l.personal));

  /** A search for users appears from this many. */
  protected readonly userSearchFrom = 10;
  protected readonly userQuery = signal('');
  protected readonly filteredUsers = computed(() => {
    const words = searchKey(this.userQuery()).trim().split(/\s+/).filter(Boolean);
    return (this.users() ?? []).filter((user) => {
      const text = searchKey(`${user.userName} ${user.displayName ?? ''}`);
      return words.every((word) => text.includes(word));
    });
  });

  /** The user whose access is being edited, and the levels chosen so far. */
  protected readonly accessFor = signal<User | null>(null);
  protected readonly accessDraft = signal<AccessLevels>({});

  /** How many libraries a user reaches, and in how many they only listen. */
  protected accessSummary(user: User): { count: number; total: number; listen: number } {
    const levels = Object.values(
      levelsOf(this.accessLibraries(), user.libraryIds, user.readOnlyLibraryIds),
    );
    return {
      count: levels.filter((l) => l !== 'none').length,
      total: levels.length,
      listen: levels.filter((l) => l === 'listen').length,
    };
  }

  protected openAccess(user: User): void {
    this.accessDraft.set(
      levelsOf(this.accessLibraries(), user.libraryIds, user.readOnlyLibraryIds),
    );
    this.accessFor.set(user);
  }

  /** Saves the user's access to every library at once. */
  protected async saveAccess(): Promise<void> {
    const user = this.accessFor();
    if (!user) return;
    await this.run(async () => {
      const result = await this.gql.mutate(SetUserLibrariesMutation, {
        id: user.id,
        input: grantsOf(this.accessLibraries(), this.accessDraft()),
      });
      const account = result.setUserLibraries;
      this.users.update(
        (users) =>
          users?.map((u) =>
            u.id === user.id
              ? {
                  ...u,
                  libraryIds: account.libraryIds,
                  readOnlyLibraryIds: account.readOnlyLibraryIds,
                }
              : u,
          ) ?? null,
      );
      this.accessFor.set(null);
      toast.success(this.i18n.t('users.accessSaved', { name: user.userName }));
    });
  }

  async ngOnInit(): Promise<void> {
    await this.load();
  }

  protected canEdit(user: User): boolean {
    return this.auth.isAdmin() || user.role !== 'ADMIN';
  }

  protected openCreate(): void {
    this.formError.set(null);
    this.form.set({
      id: null,
      userName: '',
      displayName: '',
      password: '',
      role: 'USER',
      permissions: [],
      isDisabled: false,
      levels: levelsOf(this.accessLibraries(), [], []),
    });
  }

  protected openEdit(user: User): void {
    this.formError.set(null);
    this.form.set({
      id: user.id,
      userName: user.userName,
      displayName: user.displayName ?? '',
      password: '',
      role: user.role,
      permissions: [...user.permissions],
      isDisabled: user.isDisabled,
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

  protected async saveForm(): Promise<void> {
    const form = this.form();
    if (!form) return;

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
      toast.success(this.i18n.t('users.saved'));
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
