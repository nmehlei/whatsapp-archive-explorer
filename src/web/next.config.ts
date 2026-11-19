import type { NextConfig } from "next";

// Static export, embedded into the .NET executable and served from 127.0.0.1.
// No next/font, no remote images: the UI must never load anything from the internet.
const config: NextConfig = {
  output: "export",
  images: { unoptimized: true },
  poweredByHeader: false,
  reactStrictMode: true,
  productionBrowserSourceMaps: false,
  // Pin the project root so a stray lockfile higher up (e.g. in the home folder) is never picked up.
  turbopack: { root: import.meta.dirname },
  outputFileTracingRoot: import.meta.dirname,
};

export default config;
