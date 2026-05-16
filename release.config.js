module.exports = {
  branches: ['main'],
  tagFormat: 'v${version}',
  plugins: [
    [
      '@semantic-release/commit-analyzer',
      {
        preset: 'angular',
        // Override the default Angular ruleset: in this repo a refactor is a
        // structural improvement worth shipping as a minor bump (not a no-op).
        // Keeps fix/feat/BREAKING semantics unchanged.
        releaseRules: [{ type: 'refactor', release: 'minor' }],
      },
    ],
    [
      '@semantic-release/release-notes-generator',
      {
        preset: 'angular',
        // Surface refactor commits in the generated changelog under their own
        // section — the default Angular preset hides them entirely.
        presetConfig: {
          types: [
            { type: 'feat', section: 'Features' },
            { type: 'fix', section: 'Bug Fixes' },
            { type: 'perf', section: 'Performance Improvements' },
            { type: 'refactor', section: 'Refactors' },
            { type: 'revert', section: 'Reverts' },
          ],
        },
      },
    ],
    // Render the generated release notes to HTML and stash them in
    // build/changelog-latest.html so the IntelliJ Platform Gradle Plugin's
    // changeNotes provider (see build.gradle.kts) can inject them into the
    // patched plugin.xml at publishPlugin time. Must run BEFORE the exec
    // plugin so the file exists when ./gradlew publishPlugin starts.
    ['./scripts/release-notes-writer.mjs', {}],
    [
      '@semantic-release/exec',
      {
        verifyConditionsCmd:
          'test -n "$JETBRAINS_MARKETPLACE_TOKEN" || (echo "JETBRAINS_MARKETPLACE_TOKEN env not set" && exit 1)',
        // prepare only bumps the version; publishPlugin depends on buildPlugin so it'll
        // produce the zip we need for both Marketplace upload and the GH release asset.
        prepareCmd:
          'sed -i "s/^pluginVersion=.*/pluginVersion=${nextRelease.version}/" gradle.properties',
        publishCmd: './gradlew publishPlugin -PpluginVersion=${nextRelease.version}',
      },
    ],
    [
      '@semantic-release/git',
      {
        assets: ['gradle.properties'],
        message: 'chore(release): ${nextRelease.version} [skip ci]\n\n${nextRelease.notes}',
      },
    ],
    [
      '@semantic-release/github',
      {
        assets: [
          { path: 'build/distributions/*.zip', label: 'Rider-ILSpy ${nextRelease.gitTag}' },
        ],
      },
    ],
  ],
};
