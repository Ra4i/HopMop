// Admin inquiries — click a message card to expand it.
// The server renders every card open, so the page is fully readable with JS
// disabled; this script is what collapses them and wires up the toggling.
document.addEventListener('DOMContentLoaded', function () {
  var list = document.getElementById('inquiry-list');
  // Element.closest is what drives the delegated handler below; without it the
  // cards simply stay open rather than becoming un-openable.
  if (!list || !Element.prototype.closest) return;

  var cards = list.querySelectorAll('.inq-card');
  if (!cards.length) return;

  // Only claim the cards are collapsible once we know we can collapse them.
  list.classList.add('is-collapsible');

  for (var n = 0; n < cards.length; n++) {
    var head = cards[n].querySelector('.inq-head');
    if (head) head.setAttribute('aria-expanded', 'false');
  }

  // One delegated listener, so the cost does not grow with the message count.
  list.addEventListener('click', function (e) {
    var head = e.target instanceof Element ? e.target.closest('.inq-head') : null;
    if (!head) return;

    var card = head.closest('.inq-card');
    if (!card) return;

    var isOpen = card.classList.toggle('is-open');
    head.setAttribute('aria-expanded', isOpen ? 'true' : 'false');
  });
});
