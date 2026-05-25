using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using AutoApplicator.Application.DTOs;
using AutoApplicator.Application.Interfaces;
using AutoApplicator.Presentation.Components.ViewModels;

namespace AutoApplicator.App.Components.Pages.Questions;

public partial class Questions
{
    [Inject] private QuestionListViewModel ViewModel { get; set; } = default!;
    [Inject] private INotificationService NotificationService { get; set; } = default!;

    private Dictionary<Guid, string> _answers = [];
    private Dictionary<Guid, bool> _checkboxAnswers = [];
    private HashSet<Guid> _modifiedQuestionIds = [];
    private bool _showAnswered;
    private HashSet<Guid> _expandedCardIds = [];

    protected override async Task OnInitializedAsync()
    {
        await ViewModel.LoadQuestionsAsync();
        InitializeAnswerDictionaries();
    }

    private void InitializeAnswerDictionaries()
    {
        _answers = ViewModel.Questions.ToDictionary(q => q.Id, q => q.Answer);
        _checkboxAnswers = ViewModel.Questions.ToDictionary(q => q.Id, q => !string.IsNullOrEmpty(q.Answer));
        _modifiedQuestionIds.Clear();
    }

    private void ToggleAnsweredSection()
    {
        _showAnswered = !_showAnswered;

        if (_showAnswered)
        {
            _expandedCardIds = ViewModel.Answered.Select(q => q.Id).ToHashSet();
        }
        else
        {
            _expandedCardIds.Clear();
        }
    }

    private void HandleAnsweredSectionKeyDown(KeyboardEventArgs e)
    {
        if (e.Key is "Enter" or " ")
        {
            ToggleAnsweredSection();
        }
    }

    private void ToggleCardExpansion(Guid questionId)
    {
        if (_expandedCardIds.Contains(questionId))
            _expandedCardIds.Remove(questionId);
        else
            _expandedCardIds.Add(questionId);
    }

    private void MarkQuestionAsModified(Guid questionId)
    {
        _modifiedQuestionIds.Add(questionId);
    }

    private async Task SaveSingleAnswer(Guid questionId)
    {
        try
        {
            var question = ViewModel.Questions.FirstOrDefault(q => q.Id == questionId);
            if (question is null) return;

            var answer = question.FieldType == "Checkbox"
                ? (_checkboxAnswers.GetValueOrDefault(questionId) ? "Yes" : "")
                : (_answers.GetValueOrDefault(questionId) ?? "");

            if (question.FieldType != "Checkbox" && string.IsNullOrEmpty(answer))
            {
                NotificationService.Add(
                    NotificationType.Warning,
                    "Warning",
                    "Please provide an answer before saving.");
                return;
            }

            await ViewModel.SaveAnswerAsync(questionId, answer);
            _modifiedQuestionIds.Remove(questionId);
            InitializeAnswerDictionaries();

            NotificationService.Add(
                NotificationType.Success,
                "Saved",
                "Answer saved successfully.");
        }
        catch (Exception ex)
        {
            NotificationService.Add(
                NotificationType.Error,
                "Error",
                $"Failed to save answer: {ex.Message}");
        }
    }

    private async Task SaveAllAnswers()
    {
        var errors = new List<string>();

        foreach (var questionId in _modifiedQuestionIds)
        {
            try
            {
                var question = ViewModel.Questions.FirstOrDefault(q => q.Id == questionId);
                if (question is null) continue;

                var answer = question.FieldType == "Checkbox"
                    ? (_checkboxAnswers.GetValueOrDefault(questionId) ? "Yes" : "")
                    : (_answers.GetValueOrDefault(questionId) ?? "");

                await ViewModel.SaveAnswerAsync(questionId, answer);
            }
            catch (Exception ex)
            {
                errors.Add($"Question {questionId}: {ex.Message}");
            }
        }

        _modifiedQuestionIds.Clear();

        if (errors.Count == 0)
        {
            NotificationService.Add(
                NotificationType.Success,
                "Saved",
                "All answers saved successfully.");
        }
        else
        {
            NotificationService.Add(
                NotificationType.Warning,
                "Partial Save",
                $"{errors.Count} question(s) failed to save.");
        }

        InitializeAnswerDictionaries();
    }

    private async Task DeleteQuestion(Guid questionId)
    {
        try
        {
            await ViewModel.DeleteQuestionAsync(questionId);
            _modifiedQuestionIds.Remove(questionId);
            InitializeAnswerDictionaries();

            NotificationService.Add(
                NotificationType.Success,
                "Deleted",
                "Question deleted successfully.");
        }
        catch (Exception ex)
        {
            NotificationService.Add(
                NotificationType.Error,
                "Error",
                $"Failed to delete question: {ex.Message}");
        }
    }

    private static string GetQuestionTypeDisplayText(string fieldType) => fieldType switch
    {
        "Select" => "Select",
        "Radio" => "Radio",
        "Checkbox" => "Checkbox",
        "File" => "File",
        _ => "Input"
    };

    private static string TruncateAnswerForPreview(string answer, int maxLength = 60)
    {
        if (string.IsNullOrEmpty(answer)) return "";
        return answer.Length > maxLength ? answer[..maxLength] + "…" : answer;
    }

    private List<OptionItem> GetDropdownOptions(List<string>? options)
    {
        return options?
            .Where(o => !string.IsNullOrWhiteSpace(o))
            .Select(o => new OptionItem { Text = o, Value = o })
            .ToList() ?? [];
    }

    private sealed class OptionItem
    {
        public string Text { get; set; } = "";
        public string Value { get; set; } = "";
    }
}
