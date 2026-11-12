import {
  Activity,
  Building2,
  LayoutDashboard,
  Receipt,
  ScrollText,
  Settings,
  ShieldCheck,
  UserCog,
  UsersRound,
  Webhook,
  type LucideIcon,
} from "lucide-react";
import type { ParseKeys } from "i18next";
import {
  AuditingPermissions,
  BillingPermissions,
  IdentityPermissions,
  MultitenancyPermissions,
  WebhooksPermissions,
} from "@/lib/permissions";
import { isModuleEnabled, type ModuleKey } from "@/lib/modules";

/**
 * A single nav destination. `label` is a `nav` namespace translation key
 * (not display text) — render sites resolve it via `t(item.label)`.
 */
export type NavSpec = {
  to: string;
  label: ParseKeys<"nav">;
  icon: LucideIcon;
  /** One or more permissions the user must hold to see this item. */
  perms?: readonly string[];
  /**
   * UI module this item belongs to. Hidden when the deployment lists the key in
   * `disabledModules` (config.json), independently of the user's permissions —
   * permissions are per-role, this is per-deployment. Items without a `module`
   * are never hidden this way. See lib/modules.ts.
   */
  module?: ModuleKey;
};

/** A collapsible section that groups related NavSpecs. `caption` is a `nav` key. */
export type NavSection = {
  id: string;
  caption: ParseKeys<"nav">;
  icon: LucideIcon;
  items: NavSpec[];
};

// ─── Top-level singletons ────────────────────────────────────────────────────

export const topNavTop: NavSpec[] = [
  { to: "/", label: "overview", icon: LayoutDashboard },
];

export const topNavBottom: NavSpec[] = [
  { to: "/settings", label: "settings", icon: Settings },
];

// ─── Section accordions ──────────────────────────────────────────────────────

export const sections: NavSection[] = [
  {
    id: "multitenancy",
    caption: "tenants",
    icon: Building2,
    items: [
      {
        to: "/tenants",
        label: "tenants",
        icon: Building2,
        perms: [MultitenancyPermissions.Tenants.View],
        module: "multitenancy",
      },
    ],
  },
  {
    id: "identity",
    caption: "identity",
    icon: UsersRound,
    items: [
      {
        to: "/users",
        label: "users",
        icon: UsersRound,
        perms: [IdentityPermissions.Users.View],
      },
      {
        to: "/roles",
        label: "roles",
        icon: ShieldCheck,
        perms: [IdentityPermissions.Roles.View],
      },
      {
        to: "/impersonation",
        label: "impersonation",
        icon: UserCog,
        perms: [IdentityPermissions.Impersonation.View],
        module: "impersonation",
      },
    ],
  },
  {
    id: "operations",
    caption: "operations",
    icon: Activity,
    items: [
      {
        to: "/billing",
        label: "billing",
        icon: Receipt,
        perms: [BillingPermissions.View],
        module: "billing",
      },
      {
        to: "/webhooks",
        label: "webhooks",
        icon: Webhook,
        perms: [WebhooksPermissions.Subscriptions.View],
        module: "webhooks",
      },
      {
        to: "/audits",
        label: "audits",
        icon: ScrollText,
        perms: [AuditingPermissions.AuditTrails.View],
        module: "auditing",
      },
      {
        to: "/health",
        label: "health",
        icon: Activity,
        module: "health",
      },
    ],
  },
];

// ─── Helpers ─────────────────────────────────────────────────────────────────

/** Find the section id whose items contain the given path (best prefix match). */
export function findSectionForPath(pathname: string): string | null {
  let bestId: string | null = null;
  let bestLen = 0;
  for (const s of sections) {
    for (const item of s.items) {
      if (
        (item.to === "/" && pathname === "/") ||
        (item.to !== "/" && pathname.startsWith(item.to))
      ) {
        if (item.to.length > bestLen) {
          bestLen = item.to.length;
          bestId = s.id;
        }
      }
    }
  }
  return bestId;
}

/** Returns true when the given NavSpec is the active route. */
export function isNavItemActive(item: NavSpec, pathname: string): boolean {
  if (item.to === "/") return pathname === "/";
  return pathname === item.to || pathname.startsWith(`${item.to}/`);
}

/**
 * Filter nav items by the deployment's enabled modules AND the user's granted
 * permissions. Both sidebar.tsx and mobile-nav.tsx route through here, so the
 * module gate covers desktop and the mobile drawer from this one place; each
 * then drops any section left with no items.
 */
export function filterNavSpec(items: NavSpec[], granted: readonly string[]): NavSpec[] {
  return items.filter((item) => {
    if (!isModuleEnabled(item.module)) return false;
    if (!item.perms || item.perms.length === 0) return true;
    return item.perms.every((p) => granted.includes(p));
  });
}
