"use client";

import { useUi } from "@/lib/ui";
import type { Status } from "@/lib/types";

export function IndexingScreen({ status }: { status: Status | null }) {
  const { t } = useUi();
  const total = status?.filesTotal ?? 0;
  const done = status?.filesDone ?? 0;
  return (
    <main className="indexing">
      <h1>{status?.state === "error" ? t("indexingError") : t("indexingTitle")}</h1>
      {status?.state !== "error" && (
        <>
          <progress max={Math.max(total, 1)} value={done} />
          {total > 0 && <p>{t("indexingDetail", { done, total })}</p>}
          <p className="muted">{t("indexingNote")}</p>
        </>
      )}
    </main>
  );
}
