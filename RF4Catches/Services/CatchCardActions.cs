namespace RF4Catches.Services;

public static class CatchCardActions
{
    public static string DeleteButton(int catchId) => $"""
                                                       <button type="button"
                                                               class="btn btn-circle btn-ghost btn-sm text-base-content/40 hover:text-error"
                                                               hx-delete="/api/catch/{catchId}"
                                                               hx-target="closest [data-catch-wrapper]"
                                                               hx-swap="delete swap:150ms"
                                                               data-confirm="Delete this catch?"
                                                               title="Delete capture"
                                                               aria-label="Delete capture">
                                                         <svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24"
                                                              stroke-width="1.8" stroke="currentColor" class="h-4 w-4">
                                                           <path stroke-linecap="round" stroke-linejoin="round" d="M6 18 18 6M6 6l12 12" />
                                                         </svg>
                                                       </button>
                                                       """;
}