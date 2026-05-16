// Local semantic-release plugin that renders nextRelease.notes (markdown,
// produced by @semantic-release/release-notes-generator) into HTML and writes
// it to a known location. The IntelliJ Platform Gradle Plugin's
// `pluginConfiguration.changeNotes` provider (see build.gradle.kts) reads
// that file at publishPlugin time, so the Marketplace "What's New" section
// always reflects the same notes that ship in the GitHub release.
//
// Plugin order in release.config.js matters: this MUST run after
// release-notes-generator (which populates nextRelease.notes) and before
// the @semantic-release/exec entry that invokes ./gradlew publishPlugin.

import { mkdir, writeFile } from "node:fs/promises";
import { dirname, resolve } from "node:path";
import { marked } from "marked";

const DEFAULT_MARKDOWN_PATH = "build/release-notes.md";
const DEFAULT_HTML_PATH = "build/changelog-latest.html";

// Pure helper exported for testing. Returns { markdownPath, htmlPath, html }.
// `notes` is the markdown body produced by release-notes-generator. When the
// release is a no-op (notes is null/undefined/empty), we still write empty
// files so the gradle provider has a stable read target instead of an
// existence check.
export async function writeChangelogFiles(notes, {
    markdownPath = DEFAULT_MARKDOWN_PATH,
    htmlPath = DEFAULT_HTML_PATH,
    cwd = process.cwd(),
} = {}) {
    const mdAbs = resolve(cwd, markdownPath);
    const htmlAbs = resolve(cwd, htmlPath);
    const markdown = notes ?? "";
    // marked's GFM defaults: tables, autolinks, task lists — all fine for
    // changelog content.
    const html = marked.parse(markdown);
    await mkdir(dirname(mdAbs), { recursive: true });
    await mkdir(dirname(htmlAbs), { recursive: true });
    await writeFile(mdAbs, markdown, "utf8");
    await writeFile(htmlAbs, html, "utf8");
    return { markdownPath: mdAbs, htmlPath: htmlAbs, html };
}

// semantic-release `prepare` lifecycle hook. Runs after version is determined
// and notes are generated, before plugins like @semantic-release/exec invoke
// the actual build / publish commands.
export async function prepare(pluginConfig, context) {
    const { nextRelease, logger } = context;
    const result = await writeChangelogFiles(nextRelease?.notes, {
        markdownPath: pluginConfig?.markdownPath ?? DEFAULT_MARKDOWN_PATH,
        htmlPath: pluginConfig?.htmlPath ?? DEFAULT_HTML_PATH,
        cwd: context.cwd ?? process.cwd(),
    });
    logger.log(
        `release-notes-writer: wrote markdown to ${result.markdownPath} and HTML to ${result.htmlPath} (${result.html.length} bytes)`,
    );
}
