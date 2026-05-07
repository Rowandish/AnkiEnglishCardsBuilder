# Anki English Cards Builder

Avalonia desktop app for generating Anki-importable English vocabulary cards.

## Workflow

1. Paste English words separated by commas, semicolons, or new lines.
2. Configure the LLM provider in `Settings`.
3. Generate cards with meaning, definition, example sentence, translation, usage notes, and synonyms.
4. Review and edit every field in the preview grid.
5. Export a TSV file and import it into Anki.

The exported file includes Anki headers:

```text
#separator:tab
#html:true
#tags:english vocabulary
```

The TSV uses Anki's Basic note shape: column 1 is `Front` and column 2 is `Back`. `Front` contains the English word; `Back` contains the other fields concatenated as formatted HTML.

## Provider architecture

Card generation is behind `ICardEnrichmentProvider`. The first implementation uses OpenAI through the Responses API with Structured Outputs. Additional providers can be added by implementing the same interface and extending `CardEnrichmentProviderFactory`.

Settings are stored locally under the current user's application data folder.
