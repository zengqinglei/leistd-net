import { readFileSync } from 'node:fs';

const manifests = [
  'template/frontend/package.json',
  'template/.template.config/localization/frontend/package.json',
];

for (const path of manifests) {
  const source = readFileSync(new URL(`../${path}`, import.meta.url), 'utf8');
  const manifest = JSON.parse(source.replace(/^\s*\/\/\s*#.*$/gm, ''));

  for (const section of ['dependencies', 'devDependencies']) {
    const names = Object.keys(manifest[section]);
    // npm sorts dependency keys with localeCompare using the English locale.
    const sorted = names.toSorted((left, right) => left.localeCompare(right, 'en'));
    const firstDifference = names.findIndex((name, index) => name !== sorted[index]);
    if (firstDifference !== -1) {
      console.error(
        `${path} ${section}: expected ${sorted[firstDifference]} before ${names[firstDifference]}.`,
      );
      process.exitCode = 1;
    }
  }
}
