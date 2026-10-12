import type { CodegenConfig } from '@graphql-codegen/cli';

// Typed GraphQL operations from the committed server contract. Run `npm run codegen` after changing either an
// operation in src/ or docs/api/schema.graphql; the generated files are committed.
const config: CodegenConfig = {
  schema: '../../docs/api/schema.graphql',
  documents: ['src/**/*.ts', '!src/app/graphql/generated/**'],
  ignoreNoDocuments: true,
  generates: {
    'src/app/graphql/generated/': {
      preset: 'client',
      presetConfig: { fragmentMasking: false },
      config: {
        useTypeImports: true,
        enumsAsTypes: true,
        scalars: { DateTime: 'string', ID: 'string', Long: 'number' },
      },
    },
  },
};

export default config;
