"use client";

import { createContext, useContext } from "react";
import { translator, type Locale, type Translate } from "./i18n";

export interface Ui {
  locale: Locale;
  /** BCP 47 tag for Intl formatting (the browser's own language, e.g. "de-AT"). */
  intl: string;
  t: Translate;
}

export const UiContext = createContext<Ui>({ locale: "en", intl: "en", t: translator("en") });

export function useUi(): Ui {
  return useContext(UiContext);
}
