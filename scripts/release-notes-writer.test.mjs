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
import { prepare, sanitizeReleaseNotes, writeChangelogFiles } from "./release-notes-writer.mjs";

// Realistic shape of release-notes-generator's angular preset output: a
// linked version + ISO date heading line, blank line, then sections. Used
// across several sanitize tests below.
const SEMREL_NOTES_SAMPLE = `## [1.2.0](https://github.com/foo/bar/compare/v1.1.5...v1.2.0) (2026-05-15)

### Bug Fixes

* fix(plugin): regression ([abc1234](https://example/abc1234))
`;

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

test("sanitizeReleaseNotes strips the linked version+date heading line", () => {
    const cleaned = sanitizeReleaseNotes(SEMREL_NOTES_SAMPLE);
    assert.ok(!cleaned.includes("1.2.0"), "version label must be stripped");
    assert.ok(!cleaned.includes("2026-05-15"), "release date must be stripped");
    assert.ok(cleaned.startsWith("### Bug Fixes"), "first surviving line must be the first section heading");
});

test("sanitizeReleaseNotes strips plain '# 1.2.0 (date)' heading too", () => {
    const cleaned = sanitizeReleaseNotes("# 1.2.0 (2026-05-15)\n\n### Features\n\n* feat: thing\n");
    assert.ok(cleaned.startsWith("### Features"));
});

test("sanitizeReleaseNotes left-trims all leading newlines and whitespace", () => {
    const cleaned = sanitizeReleaseNotes("\n\n\n   \n## [1.0.0](url) (2026-01-01)\n\n\n### Features\n");
    assert.equal(cleaned, "### Features\n");
});

test("sanitizeReleaseNotes leaves non-version section headings intact when there is no version line", () => {
    const cleaned = sanitizeReleaseNotes("### Features\n\n* feat: thing\n");
    assert.equal(cleaned, "### Features\n\n* feat: thing\n");
});

test("sanitizeReleaseNotes returns empty string for null/undefined/empty", () => {
    assert.equal(sanitizeReleaseNotes(null), "");
    assert.equal(sanitizeReleaseNotes(undefined), "");
    assert.equal(sanitizeReleaseNotes(""), "");
});

test("writeChangelogFiles output starts directly at the first section heading", async () => {
    await withTempCwd(async (dir) => {
        const { html, markdownPath } = await writeChangelogFiles(SEMREL_NOTES_SAMPLE, { cwd: dir });
        // No leading whitespace, no trace of the version label / date.
        assert.ok(html.startsWith("<h3"), `expected output to start with <h3>, got: ${html.slice(0, 40)}`);
        assert.ok(!html.includes("1.2.0"));
        assert.ok(!html.includes("2026-05-15"));
        const writtenMd = await readFile(markdownPath, "utf8");
        assert.ok(writtenMd.startsWith("### Bug Fixes"));
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
