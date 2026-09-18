using System.Globalization;
using System.Text.RegularExpressions;

namespace Needly.Domain;

/// <summary>The outcome of parsing an inbox search query with <see cref="ActionQueryParser"/>.</summary>
public sealed class ActionQueryParseResult
{
    private ActionQueryParseResult(ActionFilter? filter, string? error, int errorPosition, string? errorContext)
    {
        Filter = filter;
        Error = error;
        ErrorPosition = errorPosition;
        ErrorContext = errorContext;
    }

    /// <summary>Gets a value indicating whether the query parsed to a valid <see cref="ActionFilter"/>.</summary>
    public bool IsSuccess => Error is null;

    /// <summary>Gets the parsed filter, or <see langword="null"/> when parsing failed.</summary>
    public ActionFilter? Filter { get; }

    /// <summary>Gets a specific, human-readable parse error message, or <see langword="null"/> on success.</summary>
    public string? Error { get; }

    /// <summary>Gets the zero-based character offset in the query where the error was detected, or -1 on success.</summary>
    public int ErrorPosition { get; }

    /// <summary>Gets a short snippet of the query around <see cref="ErrorPosition"/> with a caret marker, or <see langword="null"/> on success.</summary>
    public string? ErrorContext { get; }

    /// <summary>Creates a successful result.</summary>
    public static ActionQueryParseResult Success(ActionFilter filter) => new(filter, null, -1, null);

    /// <summary>Creates a failed result carrying a specific error message and position.</summary>
    public static ActionQueryParseResult Failure(string message, int position, string context) =>
        new(null, message, position, context);
}

/// <summary>
/// Parses free-text and structured inbox search queries (qualifiers plus boolean composition) into an
/// <see cref="ActionFilter"/>. Introduced for issue #24 ("Inbox search: free text and structured queries").
/// </summary>
/// <remarks>
/// <para><b>Supported syntax</b>: qualifiers <c>repo:owner/name</c>, <c>org:name</c>, <c>author:login</c>,
/// <c>is:&lt;type&gt;</c>, <c>state:&lt;state&gt;</c>, <c>waiting:&gt;Nd</c> (comparison + duration),
/// <c>bot:true|false</c>, <c>self-owned:true|false</c> (whether the subject author also owns the repository);
/// boolean composition with <c>AND</c>, <c>OR</c>, <c>NOT</c> (case-insensitive) and
/// parentheses; bare words and <c>"quoted phrases"</c> become free-text terms. Adjacent terms with no explicit
/// operator are implicitly ANDed, e.g. <c>repo:a is:review</c> means <c>repo:a AND is:review</c>.</para>
///
/// <para><b>A deliberate architectural ceiling</b>: <see cref="ActionFilter"/> is a flat conjunction ("AND")
/// of independent per-field criteria, each of which is itself an inclusion ("OR") set (see the type's own
/// remarks). That shape can represent:</para>
/// <list type="bullet">
/// <item>AND/OR/NOT of same-field terms, e.g. <c>is:review OR is:merge</c>, <c>NOT is:review</c> (the
/// complement is taken over the known, finite <see cref="ActionType"/>/<see cref="ActionState"/> domains),
/// <c>NOT bot:true</c>.</item>
/// <item>AND across different fields, e.g. <c>repo:a author:b</c> — this is exactly what the filter natively
/// expresses.</item>
/// </list>
/// <para>It fundamentally <b>cannot</b> represent OR across different fields (e.g. <c>repo:a OR author:b</c>),
/// NOT of an unbounded-domain field such as <c>repo:</c>/<c>org:</c>/<c>author:</c> (there is no way to
/// enumerate "every repository except this one"), or NOT of a compound (multi-field) expression. Queries that
/// require any of these produce a specific parse error explaining the limitation rather than silently
/// producing an incorrect filter. This is a known, intentional limitation of the current
/// <see cref="ActionFilter"/> contract, not a bug — see the issue #24 pull request description.</para>
///
/// <para><b>waiting:</b> only <see cref="ActionFilter.WaitingAtLeast"/> (a lower bound) exists today, so
/// <c>waiting:&gt;Nd</c> and <c>waiting:&gt;=Nd</c> map directly to it (no distinction is made between the two:
/// both set the same inclusive lower bound). <c>waiting:&lt;Nd</c> / <c>waiting:&lt;=Nd</c> would require an
/// upper-bound field that does not exist, so it is explicitly descoped with a dedicated parse error rather than
/// silently ignored or approximated.</para>
///
/// <para><b>Free text</b>: <see cref="ActionFilter.FreeText"/> is a single string, so multiple AND-combined
/// bare/quoted terms are concatenated with a single space and matched as one substring (effectively the same
/// as quoting them together). OR of two different free-text terms is not representable in a single string and
/// produces a parse error.</para>
///
/// <para><b>Not implemented</b>: <c>assignee:</c> and <c>label:</c> (mentioned in the issue as candidates) are
/// intentionally left out of this first pass — <see cref="ActionFilter"/> has no label criterion at all, and
/// its <see cref="ActionAssigneeScope"/> is a 3-value enum (Any/Me/MyTeam) where "Any" means "unconstrained"
/// rather than a real value, which makes negation and set-based matching ill-defined. Both are reported as
/// unknown qualifiers today; a future contract change would be needed to support them properly.</para>
/// </remarks>
public static class ActionQueryParser
{
    private static readonly Regex WaitingValuePattern = new(@"^(?<amount>\d+(\.\d+)?)(?<unit>[a-zA-Z]+)$", RegexOptions.Compiled);

    private static readonly Dictionary<string, ActionType> ActionTypeAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["followup"] = ActionType.FollowUp,
        ["follow-up"] = ActionType.FollowUp
    };

    /// <summary>Parses a search query into an <see cref="ActionFilter"/>.</summary>
    /// <param name="query">The raw query text. A null or whitespace-only query yields an empty (match-all) filter.</param>
    /// <returns>A successful result carrying the compiled filter, or a failure result with a specific error.</returns>
    public static ActionQueryParseResult Parse(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return ActionQueryParseResult.Success(new ActionFilter());
        }

        try
        {
            var tokens = Tokenize(query);
            var ast = new Parser(tokens).ParseQuery();
            var fragment = Compile(ast);
            return ActionQueryParseResult.Success(ToFilter(fragment));
        }
        catch (QueryParseException exception)
        {
            return ActionQueryParseResult.Failure(exception.Message, exception.Position, BuildContext(query, exception.Position));
        }
    }

    private static string BuildContext(string query, int position, int window = 12)
    {
        var clamped = Math.Clamp(position, 0, query.Length);
        var start = Math.Max(0, clamped - window);
        var end = Math.Min(query.Length, clamped + window);
        var caret = new string(' ', clamped - start) + "^";
        return $"{query[start..end]}\n{caret}";
    }

    // ----- Tokenizer -----------------------------------------------------------------------------------------

    private enum TokenKind { LParen, RParen, And, Or, Not, Qualifier, Text, End }

    private sealed record Token(TokenKind Kind, string? Key, string? Value, int Position);

    private static List<Token> Tokenize(string query)
    {
        var tokens = new List<Token>();
        var i = 0;
        var length = query.Length;

        while (i < length)
        {
            var c = query[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            if (c == '(')
            {
                tokens.Add(new Token(TokenKind.LParen, null, null, i));
                i++;
                continue;
            }

            if (c == ')')
            {
                tokens.Add(new Token(TokenKind.RParen, null, null, i));
                i++;
                continue;
            }

            if (c == '"')
            {
                var start = i;
                i++;
                var textStart = i;
                while (i < length && query[i] != '"')
                {
                    i++;
                }

                if (i >= length)
                {
                    throw new QueryParseException("Unterminated quoted phrase: missing a closing '\"'.", start);
                }

                var text = query[textStart..i];
                i++; // consume closing quote
                tokens.Add(new Token(TokenKind.Text, null, text, start));
                continue;
            }

            var wordStart = i;
            while (i < length && !char.IsWhiteSpace(query[i]) && query[i] != '(' && query[i] != ')' && query[i] != ':')
            {
                i++;
            }

            if (i < length && query[i] == ':' && i > wordStart)
            {
                var key = query[wordStart..i];
                i++; // consume ':'
                var valueStart = i;
                while (i < length && !char.IsWhiteSpace(query[i]) && query[i] != '(' && query[i] != ')')
                {
                    i++;
                }

                var value = query[valueStart..i];
                if (value.Length == 0)
                {
                    throw new QueryParseException($"Qualifier '{key}:' requires a value.", wordStart);
                }

                tokens.Add(new Token(TokenKind.Qualifier, key, value, wordStart));
                continue;
            }

            // Either no ':' was found before whitespace/parenthesis/end, or ':' was the very first
            // character (not a valid qualifier key) — keep scanning to the end of this word as plain text.
            while (i < length && !char.IsWhiteSpace(query[i]) && query[i] != '(' && query[i] != ')')
            {
                i++;
            }

            var word = query[wordStart..i];
            switch (word.ToLowerInvariant())
            {
                case "and":
                    tokens.Add(new Token(TokenKind.And, null, null, wordStart));
                    break;
                case "or":
                    tokens.Add(new Token(TokenKind.Or, null, null, wordStart));
                    break;
                case "not":
                    tokens.Add(new Token(TokenKind.Not, null, null, wordStart));
                    break;
                default:
                    tokens.Add(new Token(TokenKind.Text, null, word, wordStart));
                    break;
            }
        }

        tokens.Add(new Token(TokenKind.End, null, null, length));
        return tokens;
    }

    // ----- AST -------------------------------------------------------------------------------------------------

    private abstract record Node(int Position);
    private sealed record AndNode(Node Left, Node Right, int Position) : Node(Position);
    private sealed record OrNode(Node Left, Node Right, int Position) : Node(Position);
    private sealed record NotNode(Node Operand, int Position) : Node(Position);
    private sealed record QualifierNode(string Key, string Value, int Position) : Node(Position);
    private sealed record TextNode(string Text, int Position) : Node(Position);

    // ----- Recursive-descent parser -----------------------------------------------------------------------------
    // Grammar (precedence low to high): Or := And (OR And)* ; And := Not (AND? Not)* ; Not := NOT Not | Primary ;
    // Primary := '(' Or ')' | qualifier | text

    private sealed class Parser(IReadOnlyList<Token> tokens)
    {
        private int _index;

        private Token Current => tokens[_index];

        public Node ParseQuery()
        {
            var node = ParseOr();
            if (Current.Kind != TokenKind.End)
            {
                throw new QueryParseException(
                    Current.Kind == TokenKind.RParen
                        ? "Unmatched closing parenthesis ')'."
                        : "Unexpected text after a complete query.",
                    Current.Position);
            }

            return node;
        }

        private Node ParseOr()
        {
            var left = ParseAnd();
            while (Current.Kind == TokenKind.Or)
            {
                var position = Current.Position;
                _index++;
                if (!IsTermStart(Current.Kind))
                {
                    throw new QueryParseException("Expected a term after 'OR'.", Current.Position);
                }

                var right = ParseAnd();
                left = new OrNode(left, right, position);
            }

            return left;
        }

        private Node ParseAnd()
        {
            var left = ParseNot();
            while (Current.Kind == TokenKind.And || IsTermStart(Current.Kind))
            {
                var position = Current.Position;
                if (Current.Kind == TokenKind.And)
                {
                    _index++;
                    if (!IsTermStart(Current.Kind))
                    {
                        throw new QueryParseException("Expected a term after 'AND'.", Current.Position);
                    }
                }

                var right = ParseNot();
                left = new AndNode(left, right, position);
            }

            return left;
        }

        private static bool IsTermStart(TokenKind kind) =>
            kind is TokenKind.LParen or TokenKind.Qualifier or TokenKind.Text or TokenKind.Not;

        private Node ParseNot()
        {
            if (Current.Kind == TokenKind.Not)
            {
                var position = Current.Position;
                _index++;
                if (!IsTermStart(Current.Kind))
                {
                    throw new QueryParseException("Expected a term after 'NOT'.", Current.Position);
                }

                return new NotNode(ParseNot(), position);
            }

            return ParsePrimary();
        }

        private Node ParsePrimary()
        {
            switch (Current.Kind)
            {
                case TokenKind.LParen:
                {
                    _index++;
                    var inner = ParseOr();
                    if (Current.Kind != TokenKind.RParen)
                    {
                        throw new QueryParseException("Expected a closing parenthesis ')'.", Current.Position);
                    }

                    _index++;
                    return inner;
                }

                case TokenKind.RParen:
                    throw new QueryParseException("Unmatched closing parenthesis ')'.", Current.Position);

                case TokenKind.Qualifier:
                {
                    var token = Current;
                    _index++;
                    return new QualifierNode(token.Key!, token.Value!, token.Position);
                }

                case TokenKind.Text:
                {
                    var token = Current;
                    _index++;
                    return new TextNode(token.Value!, token.Position);
                }

                case TokenKind.And:
                    throw new QueryParseException("Unexpected 'AND'; expected a term.", Current.Position);

                case TokenKind.Or:
                    throw new QueryParseException("Unexpected 'OR'; expected a term.", Current.Position);

                case TokenKind.End:
                    throw new QueryParseException("Expected a term, but the query ended.", Current.Position);

                default:
                    throw new QueryParseException("Unexpected token; expected a term.", Current.Position);
            }
        }
    }

    private sealed class QueryParseException(string message, int position) : Exception(message)
    {
        public int Position { get; } = position;
    }

    // ----- Compilation: AST -> Fragment (a partial ActionFilter) -----------------------------------------------
    //
    // A Fragment tracks, per field, either null ("this subtree doesn't constrain this field") or a non-empty
    // set/value ("this subtree requires this field to be one of these values"). Combining two fragments:
    //   AND -> per touched field: if only one side touches it, keep that side; if both touch it, INTERSECT
    //          (both conditions must hold simultaneously) -- this is exact, not an approximation, because each
    //          field matches "candidate value is in this set". An empty intersection means the AND can never
    //          match, which is NOT representable (an empty array means "unconstrained" in ActionFilter, the
    //          opposite of "never matches") -- so it is reported as a parse error instead of silently compiling
    //          to a wildcard.
    //   OR  -> only valid when both sides touch exactly the same single field; the sets are UNIONed. OR across
    //          two different fields (or across a multi-field subtree) is the documented ceiling and is reported
    //          as a parse error.
    //   NOT -> only valid when the operand touches exactly one field. For the finite-domain enum fields
    //          (Types/States) the complement is taken over the full enum. For Bot, the two non-"Any" values are
    //          swapped. Everything else (unbounded string fields, Waiting, FreeText, or a multi-field operand)
    //          is the documented ceiling and is reported as a parse error.

    private enum FieldKind { Types, States, Repositories, Organizations, Authors, Bot, Waiting, FreeText, SelfOwned }

    private sealed class Fragment
    {
        public HashSet<ActionType>? Types { get; init; }
        public HashSet<ActionState>? States { get; init; }
        public HashSet<string>? Repositories { get; init; }
        public HashSet<string>? Organizations { get; init; }
        public HashSet<string>? Authors { get; init; }
        public BotInvolvementFilter? Bot { get; init; }
        public TimeSpan? Waiting { get; init; }
        public List<string>? FreeTextTerms { get; init; }
        public SelfOwnedRepositoryFilter? SelfOwned { get; init; }

        public HashSet<FieldKind> TouchedFields()
        {
            var touched = new HashSet<FieldKind>();
            if (Types is { Count: > 0 }) touched.Add(FieldKind.Types);
            if (States is { Count: > 0 }) touched.Add(FieldKind.States);
            if (Repositories is { Count: > 0 }) touched.Add(FieldKind.Repositories);
            if (Organizations is { Count: > 0 }) touched.Add(FieldKind.Organizations);
            if (Authors is { Count: > 0 }) touched.Add(FieldKind.Authors);
            if (Bot is not null) touched.Add(FieldKind.Bot);
            if (Waiting is not null) touched.Add(FieldKind.Waiting);
            if (FreeTextTerms is { Count: > 0 }) touched.Add(FieldKind.FreeText);
            if (SelfOwned is not null) touched.Add(FieldKind.SelfOwned);
            return touched;
        }
    }

    private static Fragment Compile(Node node) => node switch
    {
        QualifierNode qualifier => CompileQualifier(qualifier),
        TextNode text => new Fragment { FreeTextTerms = [text.Text] },
        AndNode and => MergeAnd(Compile(and.Left), Compile(and.Right), and.Position),
        OrNode or => MergeOr(Compile(or.Left), Compile(or.Right), or.Position),
        NotNode not => CompileNot(Compile(not.Operand), not.Position),
        _ => throw new InvalidOperationException($"Unhandled node type '{node.GetType().Name}'.")
    };

    private static Fragment CompileQualifier(QualifierNode node)
    {
        return node.Key.ToLowerInvariant() switch
        {
            "repo" => new Fragment { Repositories = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { node.Value } },
            "org" => new Fragment { Organizations = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { node.Value } },
            "author" => new Fragment { Authors = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { node.Value } },
            "is" => new Fragment { Types = [ParseActionType(node.Value, node.Position)] },
            "state" => new Fragment { States = [ParseActionState(node.Value, node.Position)] },
            "bot" => new Fragment { Bot = ParseBot(node.Value, node.Position) },
            "waiting" => new Fragment { Waiting = ParseWaiting(node.Value, node.Position) },
            "self-owned" => new Fragment { SelfOwned = ParseSelfOwned(node.Value, node.Position) },
            _ => throw new QueryParseException(
                $"Unknown qualifier '{node.Key}:'. Supported qualifiers: repo, org, author, is, state, waiting, " +
                "bot, self-owned.",
                node.Position)
        };
    }

    private static Fragment MergeAnd(Fragment left, Fragment right, int position) => new()
    {
        Types = IntersectOrKeep(left.Types, right.Types, "is", position),
        States = IntersectOrKeep(left.States, right.States, "state", position),
        Repositories = IntersectOrKeep(left.Repositories, right.Repositories, "repo", position),
        Organizations = IntersectOrKeep(left.Organizations, right.Organizations, "org", position),
        Authors = IntersectOrKeep(left.Authors, right.Authors, "author", position),
        Bot = MergeBotAnd(left.Bot, right.Bot, position),
        Waiting = MaxOrKeep(left.Waiting, right.Waiting),
        FreeTextTerms = ConcatOrKeep(left.FreeTextTerms, right.FreeTextTerms),
        SelfOwned = MergeSelfOwnedAnd(left.SelfOwned, right.SelfOwned, position)
    };

    private static Fragment MergeOr(Fragment left, Fragment right, int position)
    {
        var leftFields = left.TouchedFields();
        var rightFields = right.TouchedFields();
        if (leftFields.Count != 1 || !leftFields.SetEquals(rightFields))
        {
            throw new QueryParseException(
                "'OR' can only combine two conditions on the SAME qualifier (e.g. 'is:review OR is:merge'). " +
                "Combining different qualifiers with OR (e.g. 'repo:a OR author:b') cannot be expressed by the " +
                "current ActionFilter contract, which is a flat AND of single-field OR-sets — this is a known " +
                "limitation, not a bug.",
                position);
        }

        return leftFields.Single() switch
        {
            FieldKind.Types => new Fragment { Types = Union(left.Types!, right.Types!) },
            FieldKind.States => new Fragment { States = Union(left.States!, right.States!) },
            FieldKind.Repositories => new Fragment { Repositories = Union(left.Repositories!, right.Repositories!) },
            FieldKind.Organizations => new Fragment { Organizations = Union(left.Organizations!, right.Organizations!) },
            FieldKind.Authors => new Fragment { Authors = Union(left.Authors!, right.Authors!) },
            FieldKind.Waiting => new Fragment { Waiting = left.Waiting!.Value < right.Waiting!.Value ? left.Waiting : right.Waiting },
            FieldKind.Bot when left.Bot == right.Bot => new Fragment { Bot = left.Bot },
            FieldKind.Bot => throw new QueryParseException(
                "'bot:' values combined with OR must match (e.g. 'bot:true OR bot:true'); differing values " +
                "cannot be expressed as a single bot: criterion.",
                position),
            FieldKind.FreeText when left.FreeTextTerms!.SequenceEqual(right.FreeTextTerms!, StringComparer.OrdinalIgnoreCase) =>
                new Fragment { FreeTextTerms = left.FreeTextTerms },
            FieldKind.FreeText => throw new QueryParseException(
                "Free-text terms combined with OR cannot be expressed: ActionFilter.FreeText is a single " +
                "substring field with no alternation.",
                position),
            FieldKind.SelfOwned when left.SelfOwned == right.SelfOwned => new Fragment { SelfOwned = left.SelfOwned },
            FieldKind.SelfOwned => throw new QueryParseException(
                "'self-owned:' values combined with OR must match (e.g. 'self-owned:true OR self-owned:true'); " +
                "differing values cannot be expressed as a single self-owned: criterion.",
                position),
            _ => throw new InvalidOperationException()
        };
    }

    private static Fragment CompileNot(Fragment inner, int position)
    {
        var touched = inner.TouchedFields();
        if (touched.Count != 1)
        {
            throw new QueryParseException(
                "'NOT' can only negate a single qualifier (one of: is, state, bot, self-owned); the filter model " +
                "has no way to negate a combination of different qualifiers or a compound expression.",
                position);
        }

        return touched.Single() switch
        {
            FieldKind.Types => new Fragment { Types = Complement(inner.Types!) },
            FieldKind.States => new Fragment { States = Complement(inner.States!) },
            FieldKind.Bot => new Fragment
            {
                Bot = inner.Bot == BotInvolvementFilter.OnlyBots
                    ? BotInvolvementFilter.ExcludeBots
                    : BotInvolvementFilter.OnlyBots
            },
            FieldKind.SelfOwned => new Fragment
            {
                SelfOwned = inner.SelfOwned == SelfOwnedRepositoryFilter.OnlySelfOwned
                    ? SelfOwnedRepositoryFilter.ExcludeSelfOwned
                    : SelfOwnedRepositoryFilter.OnlySelfOwned
            },
            var field => throw new QueryParseException(
                $"'{FieldQualifierName(field)}:' cannot be negated with NOT: the filter model only expresses " +
                "inclusion (an OR-set of allowed values), and there is no bounded way to express \"anything but " +
                "this\" for this qualifier.",
                position)
        };
    }

    private static string FieldQualifierName(FieldKind field) => field switch
    {
        FieldKind.Repositories => "repo",
        FieldKind.Organizations => "org",
        FieldKind.Authors => "author",
        FieldKind.Waiting => "waiting",
        FieldKind.FreeText => "free text",
        FieldKind.SelfOwned => "self-owned",
        _ => field.ToString().ToLowerInvariant()
    };

    private static ActionFilter ToFilter(Fragment fragment) => new()
    {
        Types = fragment.Types?.ToArray() ?? [],
        States = fragment.States?.ToArray() ?? [],
        Repositories = fragment.Repositories?.ToArray() ?? [],
        Organizations = fragment.Organizations?.ToArray() ?? [],
        Authors = fragment.Authors?.ToArray() ?? [],
        BotInvolvement = fragment.Bot ?? BotInvolvementFilter.Any,
        SelfOwnedRepository = fragment.SelfOwned ?? SelfOwnedRepositoryFilter.Any,
        WaitingAtLeast = fragment.Waiting,
        FreeText = fragment.FreeTextTerms is { Count: > 0 } terms ? string.Join(' ', terms) : null
    };

    // ----- Field-level merge helpers ----------------------------------------------------------------------------

    private static HashSet<T>? IntersectOrKeep<T>(HashSet<T>? left, HashSet<T>? right, string qualifierName, int position)
    {
        if (left is null)
        {
            return right;
        }

        if (right is null)
        {
            return left;
        }

        var intersection = new HashSet<T>(left, left.Comparer);
        intersection.IntersectWith(right);
        if (intersection.Count == 0)
        {
            throw new QueryParseException(
                $"'{qualifierName}:' conditions combined with AND can never both match (no overlapping " +
                "values). Use OR if you meant either value.",
                position);
        }

        return intersection;
    }

    private static HashSet<T> Union<T>(HashSet<T> left, HashSet<T> right)
    {
        var union = new HashSet<T>(left, left.Comparer);
        union.UnionWith(right);
        return union;
    }

    private static HashSet<T> Complement<T>(HashSet<T> set)
        where T : struct, Enum
    {
        var all = new HashSet<T>(Enum.GetValues<T>());
        all.ExceptWith(set);
        return all;
    }

    private static BotInvolvementFilter? MergeBotAnd(BotInvolvementFilter? left, BotInvolvementFilter? right, int position)
    {
        if (left is null)
        {
            return right;
        }

        if (right is null)
        {
            return left;
        }

        if (left == right)
        {
            return left;
        }

        throw new QueryParseException(
            "'bot:' was given conflicting values combined with AND (e.g. 'bot:true AND bot:false'); a single " +
            "action cannot satisfy both.",
            position);
    }

    private static SelfOwnedRepositoryFilter? MergeSelfOwnedAnd(
        SelfOwnedRepositoryFilter? left,
        SelfOwnedRepositoryFilter? right,
        int position)
    {
        if (left is null)
        {
            return right;
        }

        if (right is null)
        {
            return left;
        }

        if (left == right)
        {
            return left;
        }

        throw new QueryParseException(
            "'self-owned:' was given conflicting values combined with AND (e.g. 'self-owned:true AND " +
            "self-owned:false'); a single action cannot satisfy both.",
            position);
    }

    private static TimeSpan? MaxOrKeep(TimeSpan? left, TimeSpan? right)
    {
        if (left is null)
        {
            return right;
        }

        if (right is null)
        {
            return left;
        }

        // AND of two lower bounds: the stricter (larger) bound must hold.
        return left.Value > right.Value ? left : right;
    }

    private static List<string>? ConcatOrKeep(List<string>? left, List<string>? right)
    {
        if (left is null)
        {
            return right;
        }

        if (right is null)
        {
            return left;
        }

        return [..left, ..right];
    }

    // ----- Qualifier value parsing -------------------------------------------------------------------------------

    private static ActionType ParseActionType(string value, int position)
    {
        if (Enum.TryParse<ActionType>(value, ignoreCase: true, out var type))
        {
            return type;
        }

        if (ActionTypeAliases.TryGetValue(value, out var aliased))
        {
            return aliased;
        }

        var known = string.Join(", ", Enum.GetNames<ActionType>().Select(n => n.ToLowerInvariant()));
        throw new QueryParseException($"Unknown 'is:' value '{value}'. Expected one of: {known}.", position);
    }

    private static ActionState ParseActionState(string value, int position)
    {
        if (Enum.TryParse<ActionState>(value, ignoreCase: true, out var state))
        {
            return state;
        }

        var known = string.Join(", ", Enum.GetNames<ActionState>().Select(n => n.ToLowerInvariant()));
        throw new QueryParseException($"Unknown 'state:' value '{value}'. Expected one of: {known}.", position);
    }

    private static BotInvolvementFilter ParseBot(string value, int position)
    {
        if (bool.TryParse(value, out var flag))
        {
            return flag ? BotInvolvementFilter.OnlyBots : BotInvolvementFilter.ExcludeBots;
        }

        throw new QueryParseException($"Unknown 'bot:' value '{value}'. Expected 'true' or 'false'.", position);
    }

    private static SelfOwnedRepositoryFilter ParseSelfOwned(string value, int position)
    {
        if (bool.TryParse(value, out var flag))
        {
            return flag ? SelfOwnedRepositoryFilter.OnlySelfOwned : SelfOwnedRepositoryFilter.ExcludeSelfOwned;
        }

        throw new QueryParseException(
            $"Unknown 'self-owned:' value '{value}'. Expected 'true' or 'false'.",
            position);
    }

    private static TimeSpan ParseWaiting(string value, int position)
    {
        string? op = null;
        foreach (var candidate in new[] { ">=", "<=", ">", "<" })
        {
            if (value.StartsWith(candidate, StringComparison.Ordinal))
            {
                op = candidate;
                break;
            }
        }

        if (op is null)
        {
            throw new QueryParseException(
                $"Malformed 'waiting:' value '{value}'. Expected a comparison operator (>, >=, <, <=) followed " +
                "by a number and unit (d, h, m, w), e.g. 'waiting:>2d'.",
                position);
        }

        if (op is "<" or "<=")
        {
            // Descoped: ActionFilter only has WaitingAtLeast (an inclusive lower bound). There is no field to
            // express an upper bound, so we reject this explicitly rather than silently ignoring it or
            // approximating it as something else.
            throw new QueryParseException(
                $"'waiting:{op}' (an upper bound) is not supported: ActionFilter only expresses a lower bound " +
                "(WaitingAtLeast). Use 'waiting:>Nd' or 'waiting:>=Nd' instead, or remove this qualifier.",
                position);
        }

        var remainder = value[op.Length..];
        var match = WaitingValuePattern.Match(remainder);
        if (!match.Success)
        {
            throw new QueryParseException(
                $"Malformed 'waiting:' duration '{value}'. Expected a number followed by a unit (d, h, m, w), " +
                "e.g. 'waiting:>2d'.",
                position);
        }

        var amount = double.Parse(match.Groups["amount"].Value, CultureInfo.InvariantCulture);
        return match.Groups["unit"].Value.ToLowerInvariant() switch
        {
            "d" => TimeSpan.FromDays(amount),
            "h" => TimeSpan.FromHours(amount),
            "m" => TimeSpan.FromMinutes(amount),
            "w" => TimeSpan.FromDays(amount * 7),
            var unit => throw new QueryParseException(
                $"Unknown time unit '{unit}' in 'waiting:{value}'. Expected one of: d, h, m, w.",
                position)
        };
    }
}
