import { Link } from "react-router-dom";
import type { ParseKeys } from "i18next";
import { useTranslation } from "react-i18next";
import { useQuery } from "@tanstack/react-query";
import {
  ArrowRight,
  Building2,
  FileText,
  LayoutDashboard,
  Receipt,
  UsersRound,
} from "lucide-react";
import { listTenants } from "@/api/tenants";
import { listInvoices, getPlans } from "@/api/billing";
import { Skeleton } from "@/components/ui/skeleton";
import { EntityPageHeader, Stat, StatStrip, ToneIconTile, type ToneIconTileTone } from "@/components/list";
import { useAuth } from "@/auth/use-auth";
import { formatNumber } from "@/lib/format";
import { cn } from "@/lib/cn";
import { isModuleEnabled, type ModuleKey } from "@/lib/modules";

/**
 * Entry-point cards, as data rather than JSX so they can be filtered the same
 * way nav items are — two of them front Billing, which a deployment may hide
 * (see lib/modules.ts). `module` omitted means always shown.
 */
type Pivot = {
  to: string;
  icon: typeof Building2;
  tone: ToneIconTileTone;
  titleKey: ParseKeys<"dashboard">;
  descriptionKey: ParseKeys<"dashboard">;
  module?: ModuleKey;
};

const PIVOTS: Pivot[] = [
  {
    to: "/tenants",
    icon: Building2,
    tone: "info",
    titleKey: "pivot.tenants.title",
    descriptionKey: "pivot.tenants.description",
    module: "multitenancy",
  },
  {
    to: "/users",
    icon: UsersRound,
    tone: "primary",
    titleKey: "pivot.users.title",
    descriptionKey: "pivot.users.description",
  },
  {
    to: "/billing/plans",
    icon: Receipt,
    tone: "success",
    titleKey: "pivot.billing.title",
    descriptionKey: "pivot.billing.description",
    module: "billing",
  },
  {
    to: "/billing/invoices",
    icon: FileText,
    tone: "warning",
    titleKey: "pivot.invoices.title",
    descriptionKey: "pivot.invoices.description",
    module: "billing",
  },
];

/**
 * DashboardPage — the operator overview. EntityPageHeader greeting,
 * four KPI stat tiles drawing from real data, then pivot cards into
 * the rest of the app. No fake "Coming soon" filler.
 */
export function DashboardPage() {
  const { t } = useTranslation("dashboard");
  const { user } = useAuth();

  // A deployment that hides a module shouldn't pay for its data either: the
  // tiles below are gated on the same keys, so an ungated fetch would only
  // produce requests nothing renders (and 403s once permissions are trimmed
  // alongside). See lib/modules.ts.
  const tenantsEnabled = isModuleEnabled("multitenancy");
  const billingEnabled = isModuleEnabled("billing");

  const tenantsQuery = useQuery({
    queryKey: ["tenants", { pageNumber: 1, pageSize: 1 }],
    queryFn: () => listTenants({ pageNumber: 1, pageSize: 1 }),
    enabled: tenantsEnabled,
  });
  const plansQuery = useQuery({
    queryKey: ["billing", "plans", { includeInactive: true }],
    queryFn: () => getPlans(true),
    enabled: billingEnabled,
  });
  const invoicesQuery = useQuery({
    queryKey: ["billing", "invoices", { pageNumber: 1, pageSize: 50 }],
    queryFn: () => listInvoices({ pageNumber: 1, pageSize: 50 }),
    enabled: billingEnabled,
  });

  const tenantsTotal = tenantsQuery.data?.totalCount;
  const plans = plansQuery.data ?? [];
  const activePlans = plans.filter((p) => p.isActive).length;
  const invoicesPage = invoicesQuery.data;
  const outstandingCount =
    invoicesPage?.items.filter((i) => i.status === "Issued").length ?? 0;

  // 1 tenants tile + 3 billing tiles; collapse the grid rather than leaving
  // an off-balance 4-column strip when a module is hidden.
  const statCount = (tenantsEnabled ? 1 : 0) + (billingEnabled ? 3 : 0);
  const statCols = statCount >= 4 ? 4 : statCount === 3 ? 3 : 2;

  const visiblePivots = PIVOTS.filter((p) => isModuleEnabled(p.module));

  const firstName = user?.name?.split(" ")[0];

  return (
    <div className="space-y-6">
      {/* ── Page header ──────────────────────────────────────────────── */}
      <div className="fsh-enter">
        <EntityPageHeader
          icon={LayoutDashboard}
          title={
            <>
              {t("title")}{firstName ? (
                <span className="text-[var(--color-muted-foreground)]">{t("greeting", { name: firstName })}</span>
              ) : null}
            </>
          }
          tone="primary"
          description={t("description")}
        />
      </div>

      {/* ── KPI stat strip ───────────────────────────────────────────── */}
      <StatStrip cols={statCols} className="fsh-enter fsh-enter-2">
        {tenantsEnabled ? (
          <Stat
            label={t("stat.tenants")}
            value={
              tenantsQuery.isLoading ? (
                <Skeleton className="h-7 w-16" />
              ) : (
                tenantsTotal != null ? formatNumber(tenantsTotal) : "—"
              )
            }
            hint={t("stat.tenantsHint")}
          />
        ) : null}
        {billingEnabled ? (
          <>
            <Stat
              label={t("stat.plans")}
              value={
                plansQuery.isLoading ? (
                  <Skeleton className="h-7 w-16" />
                ) : (
                  formatNumber(plans.length)
                )
              }
              hint={t("stat.plansActive", { count: activePlans })}
            />
            <Stat
              label={t("stat.invoices")}
              value={
                invoicesQuery.isLoading ? (
                  <Skeleton className="h-7 w-16" />
                ) : (
                  invoicesPage != null ? formatNumber(invoicesPage.items.length) : "—"
                )
              }
              hint={
                invoicesPage
                  ? t("stat.invoicesHint", { count: invoicesPage.totalCount })
                  : t("stat.invoicesLoading")
              }
            />
            <Stat
              label={t("stat.outstanding")}
              value={
                invoicesQuery.isLoading ? (
                  <Skeleton className="h-7 w-16" />
                ) : (
                  formatNumber(outstandingCount)
                )
              }
              hint={t("stat.outstandingHint")}
              tone={outstandingCount > 0 ? "warning" : "default"}
            />
          </>
        ) : null}
      </StatStrip>

      {/* ── Quick pivots ─────────────────────────────────────────────── */}
      <section className="fsh-enter fsh-enter-3 space-y-3">
        <p className="text-[11px] font-semibold uppercase tracking-wider text-[var(--color-muted-foreground)]">
          {t("entryPoints")}
        </p>
        <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
          {visiblePivots.map(({ titleKey, descriptionKey, ...pivot }) => (
            <PivotCard key={pivot.to} {...pivot} title={t(titleKey)} description={t(descriptionKey)} />
          ))}
        </div>
      </section>
    </div>
  );
}

// ─── subcomponents ───────────────────────────────────────────────────

function PivotCard({
  to,
  icon: Icon,
  tone,
  title,
  description,
}: {
  to: string;
  icon: typeof Building2;
  tone: ToneIconTileTone;
  title: string;
  description: string;
}) {
  return (
    <Link to={to} className="group block focus:outline-none">
      <div
        className={cn(
          "flex h-full flex-col gap-3 rounded-xl border border-[var(--color-border)] bg-[var(--color-card)] p-4 shadow-xs",
          "transition-colors duration-200 hover:border-[var(--color-border-strong)] hover:bg-[var(--color-accent)]",
        )}
      >
        <div className="flex items-start justify-between">
          <ToneIconTile icon={Icon} tone={tone} size="md" />
          <ArrowRight
            aria-hidden
            className="size-3.5 text-[var(--color-muted-foreground)] opacity-0 transition-all duration-200 group-hover:translate-x-0.5 group-hover:opacity-100"
          />
        </div>
        <div>
          <div className="font-display text-[14px] font-semibold tracking-tight text-[var(--color-foreground)]">
            {title}
          </div>
          <p className="mt-0.5 text-[12px] leading-snug text-[var(--color-muted-foreground)]">
            {description}
          </p>
        </div>
      </div>
    </Link>
  );
}
