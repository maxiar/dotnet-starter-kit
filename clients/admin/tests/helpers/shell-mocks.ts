import type { Page, Route } from "@playwright/test";
import { mockJsonResponse } from "./api-mocks";

/** The full root-operator permission set — enough to satisfy every RouteGuard. */
export const ADMIN_PERMS = [
  "Permissions.Tenants.View",
  "Permissions.Tenants.Create",
  "Permissions.Tenants.Update",
  "Permissions.Tenants.ViewTheme",
  "Permissions.Tenants.UpdateTheme",
  "Permissions.Tenants.UpgradeSubscription",
  "Permissions.Users.View",
  "Permissions.Users.Search",
  "Permissions.Users.Create",
  "Permissions.Users.Update",
  "Permissions.Users.Delete",
  "Permissions.Users.ManageRoles",
  "Permissions.Users.Impersonate",
  "Permissions.Roles.View",
  "Permissions.Roles.Create",
  "Permissions.Roles.Update",
  "Permissions.Roles.Delete",
  "Permissions.RoleClaims.View",
  "Permissions.RoleClaims.Update",
  "Permissions.UserRoles.View",
  "Permissions.UserRoles.Update",
  "Permissions.Sessions.View",
  "Permissions.Sessions.Revoke",
  "Permissions.Sessions.ViewAll",
  "Permissions.Sessions.RevokeAll",
  "Permissions.Impersonation.View",
  "Permissions.Impersonation.Revoke",
  "Permissions.Billing.View",
  "Permissions.Billing.Manage",
  "Permissions.AuditTrails.View",
  "Permissions.AuditTrails.ViewCrossTenant",
  "Permissions.Webhooks.View",
  "Permissions.Webhooks.Create",
  "Permissions.Webhooks.Delete",
  "Permissions.Webhooks.Test",
] as const;

export const ADMIN_PROFILE = {
  id: "u-test-1",
  userName: "rootadmin",
  email: "admin@root.com",
  firstName: "Root",
  lastName: "Admin",
  phoneNumber: "",
  isActive: true,
  emailConfirmed: true,
  twoFactorEnabled: false,
  imageUrl: null,
} as const;

/**
 * Mock every API call the authenticated admin AppShell fires on load so any
 * protected page renders cleanly. CRITICAL: /identity/permissions must echo
 * the same permission set the test seeds, because the auth context re-hydrates
 * its in-memory permissions from this endpoint after mount (RouteGuard reads
 * that). ORDERING: broad globs first, specific last; callers add page-specific
 * mocks AFTER this so they win.
 */
export async function installAdminShellMocks(
  page: Page,
  perms: readonly string[] = ADMIN_PERMS,
): Promise<void> {
  await page.route("**/negotiate**", (r: Route) => r.abort());
  await page.route("**/api/v1/realtime/**", (r: Route) => r.abort());

  await mockJsonResponse(page, "**/api/v1/notifications**", []);
  await mockJsonResponse(page, "**/api/v1/notifications/unread-count**", 0);

  await mockJsonResponse(page, "**/api/v1/identity/profile", ADMIN_PROFILE);
  await mockJsonResponse(page, "**/api/v1/identity/permissions", [...perms]);
}

/** Build a Playwright-shaped paged response body. */
export function paged<T>(
  items: T[],
  overrides: Partial<{ pageNumber: number; pageSize: number; totalCount: number; totalPages: number }> = {},
) {
  const pageSize = overrides.pageSize ?? 20;
  const totalCount = overrides.totalCount ?? items.length;
  const totalPages = overrides.totalPages ?? Math.max(1, Math.ceil(totalCount / pageSize));
  const pageNumber = overrides.pageNumber ?? 1;
  return {
    items,
    pageNumber,
    pageSize,
    totalCount,
    totalPages,
    hasPrevious: pageNumber > 1,
    hasNext: pageNumber < totalPages,
  };
}

/**
 * Override /config.json so the app boots with these UI modules hidden.
 *
 * Register BEFORE `seedAuthedSession` / navigation — `loadRuntimeConfig()`
 * fetches once at boot with `cache: "no-store"`. The body is complete on
 * purpose: `loadRuntimeConfig` maps keys explicitly, so a partial body would
 * silently reset the other settings to their defaults.
 *
 * Passing `[]` is a meaningful case, not a no-op: it asserts that an empty list
 * hides nothing (the failure mode this feature must never have).
 */
export async function withDisabledModules(
  page: Page,
  modules: readonly string[] | string,
): Promise<void> {
  await page.route("**/config.json", (route: Route) =>
    route.fulfill({
      status: 200,
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        apiBase: "",
        defaultTenant: "root",
        dashboardUrl: "http://localhost:5174",
        inactivityIdleMs: 600_000,
        inactivityWarningMs: 60_000,
        disabledModules: modules,
      }),
    }),
  );
}
