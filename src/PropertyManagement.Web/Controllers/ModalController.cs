using Microsoft.AspNetCore.Mvc;

namespace PropertyManagement.Web.Controllers;

/// <summary>
/// Shared plumbing for controllers whose forms live in modals.
///
/// The contract with <c>modal-forms.js</c> is two responses: 422 with the same partial when
/// validation failed, so the modal re-renders in place with its messages and the user keeps what
/// they typed; and 200 with a small payload naming the region to refresh when it succeeded.
/// </summary>
public abstract class ModalController : Controller
{
    /// <summary>HTTP 422. Chosen over 400 so a genuine bad request stays distinguishable.</summary>
    protected const int ValidationFailedStatusCode = StatusCodes.Status422UnprocessableEntity;

    /// <summary>
    /// Re-renders the modal body with validation messages. Returning the same partial the GET
    /// returned is what keeps the two paths from drifting apart.
    /// </summary>
    protected IActionResult ModalValidationFailed(string partialName, object model)
    {
        Response.StatusCode = ValidationFailedStatusCode;
        return PartialView(partialName, model);
    }

    /// <summary>
    /// Closes the modal and tells the page which region to re-fetch, so only the affected part
    /// of the screen changes.
    /// </summary>
    protected IActionResult ModalSucceeded(string refreshUrl, string target, string? message = null) =>
        Ok(new { refreshUrl, target, message });

    /// <summary>
    /// Copies a rule failure onto the model so it renders beside the field it belongs to, or in
    /// the summary when it is about the record as a whole.
    /// </summary>
    protected void AddError(string? key, string message) =>
        ModelState.AddModelError(key ?? string.Empty, message);
}
