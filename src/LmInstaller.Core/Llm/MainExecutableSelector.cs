using LmInstaller.Core.Models;

namespace LmInstaller.Core.Llm;

public enum SelectionStatus
{
    /// <summary>A main executable was chosen and validated; the program may proceed.</summary>
    Selected,

    /// <summary>The user must pick from <see cref="SelectionOutcome.Candidates"/>.</summary>
    NeedsUserSelection,

    /// <summary>No candidate executable was found in the archive.</summary>
    NoCandidates,
}

public sealed record SelectionOutcome
{
    public required SelectionStatus Status { get; init; }

    public MainExecutableSelection? Selection { get; init; }

    /// <summary>Rule-based best guess, offered as a default when the user must decide.</summary>
    public MainExecutableSelection? SuggestedSelection { get; init; }

    public required IReadOnlyList<CandidateExecutable> Candidates { get; init; }

    public string? Explanation { get; init; }

    /// <summary>Why automatic selection did not happen (LLM unavailable, low confidence, ...).</summary>
    public string? Diagnosis { get; init; }

    public bool RequiresUserDecision => Status != SelectionStatus.Selected;
}

public interface IMainExecutableSelector
{
    Task<SelectionOutcome> SelectAsync(SelectionContext context, CancellationToken cancellationToken = default);
}

/// <summary>
/// Coordinates the LLM decision with the rule-based fallback:
/// high-confidence LLM results are accepted, everything else is handed to the user together with
/// a suggested candidate. Manual selection therefore always remains available.
/// </summary>
public sealed class MainExecutableSelector : IMainExecutableSelector
{
    private readonly LlmMainExecutableSelector? _llmSelector;
    private readonly MainExecutableSelectionOptions _options;

    public MainExecutableSelector(
        LlmMainExecutableSelector? llmSelector = null,
        MainExecutableSelectionOptions? options = null)
    {
        _llmSelector = llmSelector;
        _options = options ?? MainExecutableSelectionOptions.Default;
    }

    public async Task<SelectionOutcome> SelectAsync(
        SelectionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var candidates = context.Candidates;
        if (candidates.Count == 0)
        {
            return new SelectionOutcome
            {
                Status = SelectionStatus.NoCandidates,
                Candidates = candidates,
                Diagnosis = "压缩包中没有找到 .exe 或其他候选可执行文件。",
            };
        }

        var suggested = BuildRuleSuggestion(candidates[0]);
        string? diagnosis = null;

        if (_llmSelector is not null)
        {
            var attempt = await _llmSelector.TrySelectAsync(context, cancellationToken).ConfigureAwait(false);
            if (attempt.Succeeded && attempt.Selection is not null)
            {
                if (attempt.Selection.Confidence >= _options.ConfidenceThreshold)
                {
                    return new SelectionOutcome
                    {
                        Status = SelectionStatus.Selected,
                        Selection = attempt.Selection,
                        SuggestedSelection = suggested,
                        Candidates = candidates,
                        Explanation = attempt.Selection.Reason,
                    };
                }

                diagnosis = $"LLM 判断置信度较低（{attempt.Selection.Confidence:0.00}），请人工确认。";
                if (string.Equals(attempt.Selection.RelativePath, suggested.RelativePath, StringComparison.OrdinalIgnoreCase))
                {
                    suggested = attempt.Selection with { Source = SelectionSource.Rules, SuggestedName = attempt.Selection.SuggestedName };
                }
                else
                {
                    suggested = attempt.Selection;
                }
            }
            else
            {
                diagnosis = attempt.Diagnosis ?? "LLM 未能给出有效结果，已退回人工选择。";
            }
        }
        else
        {
            diagnosis = "未配置 LLM，已退回人工选择（规则评分仅供参考）。";
        }

        return new SelectionOutcome
        {
            Status = SelectionStatus.NeedsUserSelection,
            SuggestedSelection = suggested,
            Candidates = candidates,
            Diagnosis = diagnosis,
        };
    }

    private static MainExecutableSelection BuildRuleSuggestion(CandidateExecutable candidate)
    {
        var confidence = candidate.RuleScore switch
        {
            >= 60 => 0.8,
            >= 30 => 0.6,
            > 0 => 0.4,
            _ => 0.2,
        };

        var reason = candidate.RuleNotes.Count > 0
            ? "规则评分：" + string.Join("；", candidate.RuleNotes.Take(3))
            : "按文件名与目录结构的规则评分。";

        return new MainExecutableSelection
        {
            RelativePath = candidate.RelativePath,
            Source = SelectionSource.Rules,
            Confidence = confidence,
            Reason = reason,
            SuggestedName = candidate.VersionInfo.BestDisplayName,
        };
    }
}
