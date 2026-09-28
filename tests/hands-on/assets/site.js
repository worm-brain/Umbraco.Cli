// Highlight the current section in the header and fill in the footer year.
document.addEventListener("DOMContentLoaded", () => {
  const path = location.pathname;
  document.querySelectorAll(".site-header nav a").forEach((a) => {
    const href = a.getAttribute("href");
    if (href !== "/" && href !== "/da/" ? path.startsWith(href) : path === href)
      a.classList.add("is-current");
  });
  const year = document.querySelector("[data-year]");
  if (year) year.textContent = new Date().getFullYear();
});
