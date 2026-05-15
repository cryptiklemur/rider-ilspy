module.exports = {
  branches: ['main'],
  tagFormat: 'v${version}',
  plugins: [
    ['@semantic-release/commit-analyzer', { preset: 'angular' }],
    ['@semantic-release/release-notes-generator', { preset: 'angular' }],
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
