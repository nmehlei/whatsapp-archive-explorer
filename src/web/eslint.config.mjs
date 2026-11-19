import next from "eslint-config-next";

const config = [
  ...next,
  { ignores: [".next/**", "out/**", "node_modules/**", "next-env.d.ts"] },
  {
    rules: {
      // Images are local archive files served by our own server; next/image optimisation needs a server we do not have.
      "@next/next/no-img-element": "off",
    },
  },
];

export default config;
