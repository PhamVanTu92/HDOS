namespace ReportingPlatform.Providers.Matching;

internal static class WildcardMatcher
{
    internal static OperationRegistration? Resolve(
        string operation,
        IReadOnlyList<OperationRegistration> registrations)
    {
        OperationRegistration? best = null;
        int bestScore = -1;
        int bestPatternLength = -1;

        foreach (var reg in registrations)
        {
            if (reg.Status != "active")
                continue;

            if (!Matches(operation, reg.OperationPattern))
                continue;

            int score = Specificity(reg.OperationPattern);
            int patLen = reg.OperationPattern.Length;

            if (score > bestScore || (score == bestScore && patLen > bestPatternLength))
            {
                best = reg;
                bestScore = score;
                bestPatternLength = patLen;
            }
        }

        return best;
    }

    internal static bool Matches(string operation, string pattern)
    {
        var opParts  = operation.Split('.');
        var patParts = pattern.Split('.');
        return MatchSegments(opParts, 0, patParts, 0);
    }

    private static bool MatchSegments(string[] op, int oi, string[] pat, int pi)
    {
        if (pi == pat.Length && oi == op.Length) return true;
        if (pi == pat.Length) return false;

        if (pat[pi] == "**")
        {
            // ** consumes 1 or more remaining segments
            for (int consumed = 1; consumed <= op.Length - oi; consumed++)
            {
                if (MatchSegments(op, oi + consumed, pat, pi + 1))
                    return true;
            }
            return false;
        }

        if (oi == op.Length) return false;

        if (pat[pi] == "*" || pat[pi] == op[oi])
            return MatchSegments(op, oi + 1, pat, pi + 1);

        return false;
    }

    private static int Specificity(string pattern)
    {
        int score = 0;
        foreach (var segment in pattern.Split('.'))
        {
            score += segment switch
            {
                "**" => 1,
                "*"  => 10,
                _    => 100,
            };
        }
        return score;
    }
}
