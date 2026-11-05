# Email conformance corpus

Cases for `EmailConformanceTests`. One line per case:

```
id ¦ source ¦ input ¦ expect ¦ canonical ¦ flags ¦ display ¦ key
```

The separator is a space, U+00A6, a space. In `input`: `\s` is a space, `\t`, `\r`, `\n`, `\\` and `\uXXXX` are what they say, and any other backslash is literal (so a quoted-pair is written as it is). `expect` is `valid`, `invalid` or `invalid:<reason id>`. `canonical`, `display` and `key` are text, `-` for none or `*` for not checked; `flags` are `EmailFacts` names joined by `|`, `-` for none or `*` for not checked. Lines starting with `#` are comments.

- `rfc5322.cases`: the examples of RFC 5322 and the grammar clauses behind them.
- `rfc3696-2047-6531.cases`: RFC 3696 section 3 with errata 246 and 1690, RFC 2047 encoded words, RFC 6531/6532.
- `rules.cases`: key rules, flags and typo detection.
- `isemail-overrides.cases`: isemail ids whose expectation is changed, with the clause that decides each.

The isemail list (`test/tests.xml` at the commit pinned in `upstream/isemail/PIN`, fetched by `tools/fetch/isemail.sh`) is read at test time and is not committed. It is distributed under the BSD 3-Clause licence, which is `upstream/isemail/LICENSE`: Copyright (c) 2016, Dominic Sayers. It is used as a list of test inputs only.
