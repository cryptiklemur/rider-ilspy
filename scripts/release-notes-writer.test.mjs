// Smoke tests for release-notes-writer. The semantic-release plugin surface
// is hard to test without a real release run, so we test the pure helper
// (writeChangelogFiles) and the `prepare` lifecycle hook against a temp dir.
//
// Run via: npm test (or `node --test scripts/release-notes-writer.test.mjs`)

import { test } from "node:test";
import assert from "node:assert/strict";
import { mkdtemp, readFile, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { prepare, writeChangelogFiles } from "./release-notes-writer.mjs";

async function withTempCwd(fn) {
    const dir = await mkdtemp(join(tmpdir(), "rider-ilspy-rnw-"));
    try {
        await fn(dir);
    } finally {
        await rm(dir, { recursive: true, force: true });
    }
}

test("writeChangelogFiles renders markdown to HTML with headings and lists", async () => {
    await withTempCwd(async (dir) => {
        const notes = "## Bug Fixes\n\n* fix(plugin): something broken ([abc1234](https://example/abc1234))\n";
        const { html, markdownPath, htmlPath } = await writeChangelogFiles(notes, { cwd: dir });
        assert.ok(html.includes("<h2"), "expected <h2> heading in rendered HTML");
        assert.ok(html.includes("<ul"), "expected <ul> list in rendered HTML");
        assert.ok(html.includes("fix(plugin): something broken"), "expected commit message body in HTML");
        const writtenMd = await readFile(markdownPath, "utf8");
        const writtenHtml = await readFile(htmlPath, "utf8");
        assert.equal(writtenMd, notes);
        assert.equal(writtenHtml, html);
    });
});

test("writeChangelogFiles handles empty/null notes without throwing", async () => {
    await withTempCwd(async (dir) => {
        const result = await writeChangelogFiles(null, { cwd: dir });
        const writtenMd = await readFile(result.markdownPath, "utf8");
        const writtenHtml = await readFile(result.htmlPath, "utf8");
        assert.equal(writtenMd, "");
        assert.equal(writtenHtml, "");
    });
});

test("writeChangelogFiles respects custom paths", async () => {
    await withTempCwd(async (dir) => {
        const result = await writeChangelogFiles("# hi", {
            cwd: dir,
            markdownPath: "out/notes.md",
            htmlPath: "out/notes.html",
        });
        assert.match(result.markdownPath, /out\/notes\.md$/);
        assert.match(result.htmlPath, /out\/notes\.html$/);
        assert.ok(result.html.includes("<h1"));
    });
});

test("prepare lifecycle hook writes files using nextRelease.notes", async () => {
    await withTempCwd(async (dir) => {
        const logs = [];
        const logger = { log: (msg) => logs.push(msg) };
        const context = {
            cwd: dir,
            nextRelease: { notes: "## What's new\n\n- alpha\n- beta\n" },
            logger,
        };
        await prepare({}, context);
        const html = await readFile(join(dir, "build/changelog-latest.html"), "utf8");
        const md = await readFile(join(dir, "build/release-notes.md"), "utf8");
        assert.ok(html.includes("<h2"));
        assert.ok(html.includes("alpha"));
        assert.ok(md.includes("- alpha"));
        assert.equal(logs.length, 1, "prepare should emit exactly one log line");
        assert.match(logs[0], /release-notes-writer: wrote/);
    });
});

test("prepare hook tolerates missing notes (e.g. dry-run with no commits)", async () => {
    await withTempCwd(async (dir) => {
        const logger = { log: () => {} };
        await prepare({}, { cwd: dir, nextRelease: {}, logger });
        const html = await readFile(join(dir, "build/changelog-latest.html"), "utf8");
        assert.equal(html, "");
    });
});
