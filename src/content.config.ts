import { defineCollection, z } from 'astro:content';
import { docsLoader } from '@astrojs/starlight/loaders';
import { docsSchema } from '@astrojs/starlight/schema';

// Nota-specific frontmatter on top of Starlight's:
//   since    — the Nota version the feature first shipped in
//   updated  — the Nota version this page was last checked against the code
//   sources  — paths in the app repo (../nota) the page is written from; the nota-docs
//              skill finds pages to update by them
//   status   — todo (stub) | draft | done
export const collections = {
  docs: defineCollection({
    loader: docsLoader(),
    schema: docsSchema({
      extend: z.object({
        since: z.string().optional(),
        updated: z.string().optional(),
        sources: z.array(z.string()).default([]),
        status: z.enum(['todo', 'draft', 'done']).default('done'),
      }),
    }),
  }),
};
