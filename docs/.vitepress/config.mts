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
      { text: "Docs", link: "/intro/what-is-outboxkit" },
    ],

    sidebar: [
      {
        text: "Intro",
        collapsed: true,
        items: [
          { text: "Quickstart", link: "/intro/quickstart" },
          { text: "What is OutboxKit?", link: "/intro/what-is-outboxkit" },
          { text: "Transactional outbox pattern", link: "/intro/transactional-outbox-pattern" },
          { text: "Concepts", link: "/intro/concepts" },
        ],
      },
      {
        text: "Core",
        collapsed: true,
        items: [
          { text: "Core overview", link: "/core/overview" },
          { text: "Producing messages", link: "/core/producing-messages" },
          { text: "Polling trigger optimization", link: "/core/polling-trigger-optimization" },
        ],
      },
      {
        text: "MySQL",
        collapsed: true,
        items: [
          { text: "MySQL provider overview", link: "/mysql/overview" },
          { text: "Polling", link: "/mysql/polling" },
        ],
      },
      {
        text: "PostgreSQL",
        collapsed: true,
        items: [
          { text: "PostgreSQL provider overview", link: "/postgresql/overview" },
          { text: "Polling", link: "/postgresql/polling" },
        ],
      },
      {
        text: "MongoDB",
        collapsed: true,
        items: [
          { text: "MongoDB provider overview", link: "/mongodb/overview" },
          { text: "Polling", link: "/mongodb/polling" },
        ],
      },
      {
        text: "Observability",
        collapsed: true,
        items: [
          { text: "Observability overview", link: "/observability/overview" },
          { text: "Built-in instrumentation", link: "/observability/built-in-instrumentation" },
          { text: "Helpers", link: "/observability/helpers" }
        ],
      },
      {
        text: "Building a provider",
        collapsed: true,
        items: [
          { text: "Building a provider overview", link: "/building-a-provider/overview" },
          { text: "Polling", link: "/building-a-provider/polling" },
          { text: "Push", link: "/building-a-provider/push" }
        ],
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
