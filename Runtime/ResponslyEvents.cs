using System;
using System.Collections.Generic;
using Responsly.Internal;

namespace Responsly
{
    /// <summary>A survey, as passed to the display guard and included in <see cref="ResponslySDK.SurveyShown"/>.</summary>
    public sealed class ResponslySurvey
    {
        public ResponslySurvey(string id, string name)
        {
            Id = id ?? string.Empty;
            Name = name ?? string.Empty;
        }

        /// <summary>The Survey ID.</summary>
        public string Id { get; private set; }

        /// <summary>The form's name in Responsly.</summary>
        public string Name { get; private set; }

        public override string ToString()
        {
            return "ResponslySurvey(" + Id + ", " + Name + ")";
        }
    }

    /// <summary>What caused a survey to be shown.</summary>
    public sealed class ResponslyTrigger
    {
        public ResponslyTrigger(string type, string name)
        {
            Type = type ?? string.Empty;
            Name = name ?? string.Empty;
        }

        /// <summary><c>event</c>, <c>screen</c>, <c>app_launch</c>, or <c>manual</c> for <see cref="ResponslySDK.ShowSurvey"/>.</summary>
        public string Type { get; private set; }

        /// <summary>The event or screen name. Empty for <c>app_launch</c> and <c>manual</c>.</summary>
        public string Name { get; private set; }

        public override string ToString()
        {
            return "ResponslyTrigger(" + Type + ", " + Name + ")";
        }
    }

    /// <summary>One answer of a completed survey.</summary>
    public sealed class ResponslyAnswer
    {
        public ResponslyAnswer(string questionId, string type, string title, string valueJson)
        {
            QuestionId = questionId ?? string.Empty;
            Type = type ?? string.Empty;
            Title = title ?? string.Empty;
            ValueJson = string.IsNullOrEmpty(valueJson) ? "null" : valueJson;
        }

        public string QuestionId { get; private set; }

        /// <summary>The question's type, for example <c>nps</c>, <c>rating</c>, <c>multiple_choice</c>, <c>text</c>.</summary>
        public string Type { get; private set; }

        /// <summary>The question text.</summary>
        public string Title { get; private set; }

        /// <summary>
        /// The answer as the survey stores it, as JSON text: <c>9</c>, <c>"some text"</c>,
        /// <c>true</c>, <c>["a","b"]</c>, <c>{"row":"a"}</c> or <c>null</c>.
        /// </summary>
        public string ValueJson { get; private set; }

        /// <summary>The answer when it is text; otherwise <c>null</c>.</summary>
        public string AsString()
        {
            return ResponslyJson.ReadString(ValueJson);
        }

        /// <summary>The answer when it is a number; otherwise <c>null</c>.</summary>
        public double? AsDouble()
        {
            return ResponslyJson.ReadDouble(ValueJson);
        }

        /// <summary>The answer when it is <c>true</c> or <c>false</c>; otherwise <c>null</c>.</summary>
        public bool? AsBool()
        {
            return ResponslyJson.ReadBool(ValueJson);
        }

        public override string ToString()
        {
            return "ResponslyAnswer(" + QuestionId + ", " + Type + ", " + ValueJson + ")";
        }
    }

    /// <summary>The survey became visible.</summary>
    public sealed class SurveyShownEventArgs : EventArgs
    {
        public SurveyShownEventArgs(ResponslySurvey survey, ResponslyTrigger trigger)
        {
            Survey = survey;
            Trigger = trigger;
        }

        public ResponslySurvey Survey { get; private set; }

        public ResponslyTrigger Trigger { get; private set; }
    }

    /// <summary>A survey matched a trigger or was requested with <c>ShowSurvey</c>, but was not shown.</summary>
    public sealed class SurveyNotShownEventArgs : EventArgs
    {
        public SurveyNotShownEventArgs(string surveyId, string reason)
        {
            SurveyId = surveyId ?? string.Empty;
            Reason = reason ?? string.Empty;
        }

        public string SurveyId { get; private set; }

        /// <summary>
        /// One of the not-shown reasons, for example <c>frequency_limit</c>, <c>audience_mismatch</c>
        /// or <c>vetoed</c>. The full list is in the API reference.
        /// </summary>
        public string Reason { get; private set; }
    }

    /// <summary>The respondent answered a question.</summary>
    public sealed class QuestionAnsweredEventArgs : EventArgs
    {
        public QuestionAnsweredEventArgs(string surveyId, string questionId, string type, string title, string valueJson)
        {
            SurveyId = surveyId ?? string.Empty;
            QuestionId = questionId ?? string.Empty;
            Type = type ?? string.Empty;
            Title = title ?? string.Empty;
            ValueJson = string.IsNullOrEmpty(valueJson) ? "null" : valueJson;
        }

        public string SurveyId { get; private set; }

        public string QuestionId { get; private set; }

        /// <summary>The question's type, for example <c>nps</c>, <c>rating</c>, <c>multiple_choice</c>, <c>text</c>.</summary>
        public string Type { get; private set; }

        /// <summary>The question text as shown to the respondent.</summary>
        public string Title { get; private set; }

        /// <summary>The answer as JSON text. See <see cref="ResponslyAnswer.ValueJson"/>.</summary>
        public string ValueJson { get; private set; }

        /// <summary>The answer when it is text; otherwise <c>null</c>.</summary>
        public string AsString()
        {
            return ResponslyJson.ReadString(ValueJson);
        }

        /// <summary>The answer when it is a number; otherwise <c>null</c>.</summary>
        public double? AsDouble()
        {
            return ResponslyJson.ReadDouble(ValueJson);
        }

        /// <summary>The answer when it is <c>true</c> or <c>false</c>; otherwise <c>null</c>.</summary>
        public bool? AsBool()
        {
            return ResponslyJson.ReadBool(ValueJson);
        }
    }

    /// <summary>The respondent submitted the survey.</summary>
    public sealed class SurveyCompletedEventArgs : EventArgs
    {
        public SurveyCompletedEventArgs(string surveyId, string responseId, IList<ResponslyAnswer> answers)
        {
            SurveyId = surveyId ?? string.Empty;
            ResponseId = responseId ?? string.Empty;
            Answers = new List<ResponslyAnswer>(answers ?? new ResponslyAnswer[0]).AsReadOnly();
        }

        public string SurveyId { get; private set; }

        /// <summary>The ID of the saved response.</summary>
        public string ResponseId { get; private set; }

        public IReadOnlyList<ResponslyAnswer> Answers { get; private set; }
    }

    /// <summary>The survey was closed before it was completed.</summary>
    public sealed class SurveyDismissedEventArgs : EventArgs
    {
        public SurveyDismissedEventArgs(string surveyId)
        {
            SurveyId = surveyId ?? string.Empty;
        }

        public string SurveyId { get; private set; }
    }

    /// <summary>The survey reported an error while it was visible.</summary>
    public sealed class SurveyErrorEventArgs : EventArgs
    {
        public SurveyErrorEventArgs(string surveyId, string code, string message)
        {
            SurveyId = surveyId ?? string.Empty;
            Code = code ?? string.Empty;
            Message = message ?? string.Empty;
        }

        public string SurveyId { get; private set; }

        /// <summary>A short error code.</summary>
        public string Code { get; private set; }

        /// <summary>A description for logs. Do not show it to users.</summary>
        public string Message { get; private set; }
    }
}
