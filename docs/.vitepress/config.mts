import { defineConfig, HeadConfig } from "vitepress";

const analyticsHeaders: HeadConfig[] =
  process.env.ENABLE_ANALYTICS === "true"
    ? [
        [
          "script",
          {
            defer: "true",
            src: "/p/js/script.js",
            "data-api": "/p/api/event",
            "data-domain": "outboxkit.yakshavefx.dev",
          },
        ],
      ]
    : [];

// https://vitepress.dev/reference/site-config
export default defineConfig({
  title: "OutboxKit",
  description: "Toolkit to implement the transactional outbox pattern",
  head: [
    ...analyticsHeaders,
    [
      "link",
      {
        rel: "apple-touch-icon",
        type: "image/png",
        size: "180x180",
        href: "/apple-icon-180x180.png",
      },
    ],
    [
      "link",
      {
        rel: "icon",
        type: "image/png",
        size: "32x32",
        href: "/favicon-32x32.png",
      },
    ],
    [
      "link",
      {
        rel: "icon",
        type: "image/png",
        size: "16x16",
        href: "/favicon-16x16.png",
      },
    ],
    ["link", { rel: "manifest", manifest: "/manifest.json" }],
  ],
  themeConfig: {
    // https://vitepress.dev/reference/default-theme-config
    nav: [
      { text: "Home", link: "/" },
      { text: "Docs", link: "/what-is-outboxkit" },
    ],

    sidebar: [
      {
        text: "TODO",
        collapsed: true,
        items: [
          { text: "What is OutboxKit?", link: "/what-is-outboxkit" },
          { text: "TODO", link: "/todo" },
        ],
      },
      {
        text: "MySQL",
        collapsed: true,
        items: [{ text: "TODO", link: "/todo" }],
      },
    ],

    socialLinks: [
      { icon: "github", link: "https://github.com/yakshavefx/outboxkit" },
    ],

    footer: {
      message: "Released under the MIT License.",
      copyright: "Copyright © João Antunes and contributors.",
    },

    search: {
      provider: "local",
    },

    editLink: {
      pattern: "https://github.com/yakshavefx/outboxkit/edit/main/docs/:path",
      text: "Suggest changes to this page",
    },
  },
  sitemap: {
    hostname: "https://outboxkit.yakshavefx.dev",
    lastmodDateOnly: false,
  },
});
