import js from '@eslint/js';
import tseslint from 'typescript-eslint';
import reactHooks from 'eslint-plugin-react-hooks';
import reactRefresh from 'eslint-plugin-react-refresh';
import eslintConfigPrettier from 'eslint-config-prettier';

export default tseslint.config(
  {
    ignores: [
      // Build output. A glob rather than a list, matching .gitignore: linting a
      // bundle means linting bundled dependency code (a stray audit output once
      // produced ~10,000 errors here), and a new output directory should not
      // have to be remembered in two places to stay out of the way.
      'dist*/',
      'node_modules/',
      'src/components/ui/',
      '*.js',
      '.claude/',
    ],
  },
  js.configs.recommended,
  ...tseslint.configs.recommended,
  {
    plugins: {
      'react-hooks': reactHooks,
      'react-refresh': reactRefresh,
    },
    rules: {
      ...reactHooks.configs.recommended.rules,
      'react-refresh/only-export-components': ['warn', { allowConstantExport: true }],
    },
  },
  eslintConfigPrettier,
);
