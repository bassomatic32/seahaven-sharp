

using System;
using System.Drawing;
using System.Security.Cryptography;



enum Rank {
	ace = 1,
	two,three,four,five,six,seven,eight,nine,ten,jack,queen,king
}

static class RankName {
	public static string GetName(Rank rank) {
		switch (rank) {
			case Rank.ace: return "A";
			case Rank.jack: return "J";
			case Rank.queen: return "Q";
			case Rank.king: return "K";
			default: return ((int)rank).ToString();
		}
	}
}

enum Suit {
	hearts=0,
	diamonds,
	clubs,
	spades
}

enum StackType {
	GOAL, TABLEAU, CELL
}

static class SuitName {
	public static string GetName(Suit suit) {
		switch (suit) {
			case Suit.hearts: return "H";
			case Suit.diamonds: return "D";
			case Suit.clubs: return "C";
			case Suit.spades: return "S";
			default: return "";
		}
	}
}

class OptionalStack<T> : Stack<T> {

	public T? OptionalPop() {
		if (this.Count() > 0) {
			return this.Pop();
		}
		return default(T);
	}

	public T? OptionalPeek() {
		if (this.Count() > 0) {
			return this.Peek();
		}
		return default(T);
	}

	public T? OptionalElementAt(int index) {
		if (index < this.Count() && index >= 0) {
			return this.ElementAt(index);
		}
		return default(T);
	}

	public T? OptionalLast() {
		if (this.Count() > 0) {
			return this.Last();
		}
		return default(T);
	}
}

class Card {
	public Rank rank;
	public Suit suit;

	public Card(Rank rank, Suit suit) {
		this.rank = rank;
		this.suit = suit;
	}

	public override string ToString() {
		return RankName.GetName(rank) + SuitName.GetName(suit);
	}

	public static int cardValue(Card? card) {
		if (card != null) {
			return (int) card.suit * 13 + (int) card.rank;
		}
		return 0;
	}

	public static string cardName(Card? card,string fallback) {
		if (card != null) {
			return card.ToString();
		}
		return fallback;
	}	

	public static System.ConsoleColor Color(Card? card) {
		if (card == null) {
			return ConsoleColor.White;
		}
		switch (card.suit) {
			case Suit.hearts:
				return ConsoleColor.DarkRed;
			case Suit.diamonds:
				return ConsoleColor.Red;
			case Suit.clubs:
				return ConsoleColor.DarkBlue;
			case Suit.spades:
				return ConsoleColor.Blue;
			default:
				return ConsoleColor.White;
		}
	}

	public static void write(Card? card,String fallback) {
		Console.ForegroundColor = Color(card);
		Console.Write(cardName(card,fallback));
		Console.ForegroundColor = ConsoleColor.Black;
	}
}

struct Tally {
	public int totalGames = 0;
	public int winnable = 0;
	public int losers = 0;
	public int abandoned = 0;

	public Tally()
	{
	}
}

// Indicates a particular stack.  ( Not a position within that stack )
struct Position {
	public int index;
	public StackType type;
}

struct Move {
	public Position source;
	public Position target;
	public int extent;
}


class Board {
	public OptionalStack<Card>[] goals = new OptionalStack<Card>[4];
	public OptionalStack<Card>[] cells = new OptionalStack<Card>[4];
	public OptionalStack<Card>[] stacks = new OptionalStack<Card>[10];

	public Board() {

		Stack<Card> deck = new Stack<Card>();
		
		foreach (Suit suit in Enum.GetValues(typeof(Suit))) {
			foreach (Rank rank in Enum.GetValues(typeof(Rank))) {
				deck.Push(new Card(rank, suit));
			}
		}

		// shuffle ... there's no built-in shuffle in C#?
		Random random = new Random();
		deck = new Stack<Card>(deck.OrderBy(x => random.Next()));

		// init each of the 10 stacks with 5 cards each
		for (int i = 0; i < 10; i++) {
			var stack = new OptionalStack<Card>();
			for (int j = 0; j < 5; j++) {
				stack.Push(deck.Pop());
			}
			stacks[i] = stack;
		}

		// append 0 length stacks for the goals and cells
		for (int i = 0; i < 4; i++) {
			goals[i] = (new OptionalStack<Card>());
			cells[i] = (new OptionalStack<Card>());
		}


		// add remaining two cards to the cells
		cells[0].Push(deck.Pop());
		cells[1].Push(deck.Pop());

	}


	// give us a unique hash value representing the current board state.  This is used to avoid repeating the same board state
	// NOTE: we consider only cells and tableau stacks.  Each of these two list of stacks are sorted by the top card of each stack to ensure that order is not considered.
	public int hashValue() {
		var cells = this.cells.Select(cell => Card.cardValue(cell.OptionalPeek())).ToArray().OrderBy(x => x).ToArray();
		
		int[][] tabs = this.stacks.Select(stack => stack.Select(
			card => Card.cardValue(card))
			.ToArray())
			.OrderBy( x => x.Count() == 0 ? 0 : x[x.Count()-1])
			.ToArray();

		SHA256Managed hasher = new SHA256Managed();
		byte[] cellBytes = cells.SelectMany(BitConverter.GetBytes).ToArray();
		byte[] digest = hasher.ComputeHash(cellBytes.Concat(tabs.SelectMany(x => x.SelectMany(BitConverter.GetBytes)).ToArray()).ToArray());
		return BitConverter.ToInt32(digest, 0);

	}

	// you cannot create a sequence of more than 5 consecutive cards if a lower card of the same suit is higher in the stack.
	// Doing so will block that suit from ever making it to the goal, because you can only move 5 cards in sequence at once
	// e.g. with stack 2H 10H 9H 8H 7H 6H, moving the 5H on the end would cause a situation where the 2H could never be freed.
	// we can ensure this doesn't happen and reduce our possibility tree
	public bool isBlockingMove(Card card, Stack<Card> targetStack, int extentLength) {
		if (targetStack.Count() + extentLength < 5) {
			return false;
		}

		var foundLower = false;
		var count = stackOrderedCount(targetStack);
		foreach (Card c in targetStack) {
			if (c.suit == card.suit && c.rank < card.rank) {
				foundLower = true;
				break;
			}
		}
		// if we found a lower card higher in the stack AND the counted sequence + extentLength ( how many cards we are moving onto the stack ) >= 5 , then its a blocking move, as it will
		// result in 6 or more cards in sequence with a lower card higher in the stack
		if ( foundLower && (count + extentLength) >= 5) {
			return true;
		}

		return false;
	}

	// returns how many cards on the top of the stack are ordered ( inclusive ).  That is, there will always be at least one, unless the stack is empty
	public int stackOrderedCount(Stack<Card> stack) {
		if (stack.Count() == 0) {
			return 0;
		}

		var count = 1;
		for (int i = 1; i < stack.Count(); i++) {
			if (Card.cardValue(stack.ElementAt(i)) == Card.cardValue(stack.ElementAt(i-1)) + 1) {
				count++;
			} else {
				break;
			}
		}

		return count;
	}

	public List<Position> findFreeCells() {
		var freeCells = new List<Position>();
		foreach (var cell in cells) {
			if (cell.Count() == 0) {
				freeCells.Add(new Position { index = Array.IndexOf(cells, cell), type = StackType.CELL });
			}
		}
		return freeCells;
	}

	public int freeCellCount() {
		return findFreeCells().Count();
	}

	// an extent is a ordered set of cards ( starting with top most ) that is less or equal to the number of freeCells+1
	// For example, the most basic extent is 1 card, and we don't need any free cells
	// we can move an extent of values 5,4,3 if there are 2 or more free cells
	// logic is simple:  move every card except the final one into the available free cells, move the final card to target, then move cards from cells back onto final card in new position
	// we will return the total number of cards in the extent, or 0 meaning there is no movable card
	public int findExtent(Stack<Card> stack) {
		var count = stackOrderedCount(stack);

		if ( count <= (freeCellCount() + 1) ) {
			return count;
		}
		return 0;
	}

	// check if the board is in a winning state
	public bool isSuccess() {
		var goalCount = goals.Aggregate(0, (acc, goal) => acc + goal.Count());
		return goalCount == 52;
	}

	// Check to see if the stack is fully ordered
	// a stack is considered to be fully ordered if any ordered sequence from the top of the stack down is made up of more than the available free cells + 1
	// ( once you've hit 6 cards, the only place you can move the top card is to the goal.  You'll fill up the available cells trying to move the whole sequence)
	public bool isFullyOrdered(Stack<Card> stack) {

		if (stack.Count() == 0) return false;

		var freeCells = freeCellCount();

		var orderedCount = stackOrderedCount(stack);

		// if the ordered count is the same as the stack count, and the root card is a king, then the stack is fully ordered.  (There's no point in moving any cards in that stack)
		if (orderedCount == stack.Count() && stack.Last().rank == Rank.king) {
			return true;
		}

		if (stack.Count() < freeCells+1) return false; // stack is too small to be fully ordered

		if (orderedCount > freeCells+1) return true; // stack is fully ordered

		return false;

		
	}
	
	// Resolve a position into a reference to a particular card stack
	public OptionalStack<Card> resolvePosition(Position position) {
		switch (position.type) {
			case StackType.GOAL:
				return goals[position.index];
			case StackType.TABLEAU:
				return stacks[position.index];
			case StackType.CELL:
				return cells[position.index];
			default:
				return null;
		}
	}

	// move a single card from one stack to another
	public void moveCard(Position source, Position target) {
		// Console.WriteLine("Moving card from " + source.index + " type " + source.type+ " to " + target.index + " type " + target.type);
		var sourceStack = resolvePosition(source);
		var targetStack = resolvePosition(target);
		// Console.WriteLine("Source Count: " + sourceStack.Count() + " Target Count: " + targetStack.Count());

		var card = sourceStack.Pop();
		targetStack.Push(card);
	}

	public bool isLegalMove(Card card,Position target, int extentLength) {
		var targetStack = resolvePosition(target);
		var targetCard = targetStack.OptionalPeek();	// the card at the top of the target stack

		if (target.type == StackType.GOAL) {
			// two conditions.  The card is an Ace, and the goal is empty
			// or the card is one higher than the top card of the goal
			if (targetCard == null) return card.rank == Rank.ace;
			
			return targetCard.suit == card.suit && targetCard.rank == card.rank - 1;
		}

		if (target.type == StackType.CELL) {
			return targetStack.Count() == 0; // cells can only hold one card, and our only condition is that it is empty
		}

		// we know that the target is a tableau stack
		// empty stacks can only accept kings
		if (targetCard == null) {
			return card.rank == Rank.king;
		}

		// for all other Tableau moves, the top of the target stack must be the same suit and be exactly one greater in rank value  ( e.g.  2H can only go on 3H)
		// it also must not be a blocking move
		
		return targetCard.suit == card.suit && targetCard.rank == card.rank + 1 && !isBlockingMove(card, targetStack, extentLength);
	}

	// find the best legal move
	public Move? findLegalMove(Position source) {
		var sourceStack = resolvePosition(source);
		if (sourceStack.Count() > 0) {  // only can move from stack with items in it

			var card = sourceStack.Peek();

			// first check the goal stacks
			foreach (var goal in goals) {
				if (isLegalMove(sourceStack.Peek(), new Position { index = Array.IndexOf(goals, goal), type = StackType.GOAL }, 1)) {
					return new Move { source = source, target = new Position { index = Array.IndexOf(goals, goal), type = StackType.GOAL }, extent = 1 };
				}
			}

			// short-circuit here if source stack is full ordered
			if (source.type == StackType.TABLEAU && isFullyOrdered(sourceStack)) {
				return null;
			}

			var extent = 0;
			if (source.type == StackType.TABLEAU) {
				extent = findExtent(sourceStack);
				if (extent > 0) {
					card = sourceStack.ElementAt(extent-1);
				} else {
					return null;  // we found no extent, and thus there is no legal move
				}
			}

			for (int i = 0; i < stacks.Count(); i++) {
				var target = new Position { index = i, type = StackType.TABLEAU };
				if (source.type == StackType.TABLEAU && source.index == i) {
					continue; // can't move to the same stack
				}
				if (isLegalMove(card, target, extent)) {
					return new Move { source = source, target = target, extent = extent };
				}
			}

			// only thing left to try is targeting a free cell
			if (source.type == StackType.CELL) return null;  // cells cannot target other cells

			var freeCells = findFreeCells();
			if (freeCells.Count() > 0 && extent <= 1) {
				return new Move { source = source, target = freeCells[0], extent = 1 };
			}

		}
		return null;
	}


}

class BoardMemory {
	Dictionary<int,bool> boardSet = new Dictionary<int,bool>();
	public int repeatsAvoided = 0;

	public BoardMemory() {
	}

	public bool registerBoard(Board board) {
		var hash = board.hashValue();
		if (boardSet.ContainsKey(hash)) {
			repeatsAvoided++;
			return true;
		}
		boardSet.Add(hash, true);
		return false;

	}

	public int size() {
		return boardSet.Count();
	}
}


class Game {
	Board board;
	int stackSize;
	int totalMoves = 0;
	Tally tally;
	public BoardMemory memory;
	Stack<Move> gameMoves = new Stack<Move>();
	bool abandoned = false;

	const int ABANDON_LIMIT = 100000;
	const int DISPLAY_LIMIT = 100;

	public Game(Tally fromTally, BoardMemory withMemory) {
		tally = fromTally;
		memory = withMemory;
		board = new Board();
	}

	public void recordMove(Position source, Position target, int extent) {
		gameMoves.Push(new Move { source = source, target = target, extent = extent });
	}

	public void moveCard(Position source, Position target, int extent) {
		recordMove(source, target, extent);
		
		board.moveCard(source, target);
		totalMoves++;

		if (totalMoves % DISPLAY_LIMIT == 0) {
			this.print();
		}

	}

	// We move an extent by moving extent-1 cards to free cells, moving the inner most card in the extent, then moving the remaining from the cells in reverse order
	// e.g. if we have an extent of values 5,4,3 moving to a target stack where top card is 6, move 3, 4 to free cells, move 5 -> target stack, then 4,3 to target stack in that order
	// this totals to (extent-1) * 2 + 1 total moves.  This amount should be used when undoing this action
	// assume there are enough free cells to do this
	public void moveExtent(Position source, Position target, int extent) {

		var freeCells = board.findFreeCells();

		if (freeCells.Count() >= ( extent -1)) {
			for (int i = 0; i < extent-1; i++) {
				var cellPosition = freeCells[i];
				moveCard(source, cellPosition, extent);
			}
			moveCard(source, target, extent);
			for (int i = extent-2; i >= 0; i--) {
				moveCard(freeCells[i], target, extent);
			}
		}
	}

	public void undoLastMove() {
		// Console.WriteLine("Undoing last move");
		if (gameMoves.Count() > 0) {
			var gameMove = gameMoves.Pop();
			board.moveCard(gameMove.target, gameMove.source);
		}
	}

	// Make the given move and recursively continue playing from the new configuration.
	// That is, we will make that move, then follow that line of the possibility tree recursively.  Otherwise, we fail out of the function
	bool moveAndPlayOne(Move move) {
		
		// for TABLEAU -> TABLEAU moves, use move extent
		if (move.extent > 1 && move.source.type == StackType.TABLEAU && move.target.type == StackType.TABLEAU) {
			moveExtent(move.source, move.target, move.extent);
		} else {
			this.moveCard(move.source, move.target, move.extent);
		}

		// move has been made, lets check some things before we move on
		if (board.isSuccess()) return true; // short-circuit here if board is in winning state

		bool repeatBoard = memory.registerBoard(board);
		if (!repeatBoard) { // this is not a board configuration we've seen before, so recursively play on
			bool success = cycleThroughCards();
			if (success) return true;
		}

		// if we've reached this point in the code then either the board was a repeat, or we failed to find a solution from this board configuration
		// in either case, the same action is taken, we undo the last move and return false
		if (move.extent > 1) {
			// undo all the moves we made in dealing with an extent
			var totalExtentMoves = (move.extent-1) * 2 + 1;
			for (int i = 0; i < totalExtentMoves; i++) {
				undoLastMove();
			}
		} else {
			undoLastMove();
		}
		// ( strictly speaking, that conditional wasn't necessary and is just an optimization)
		
		return false;
	}

	// our fundamental game loop.  Iterate over every Tableau and Cell stack, finding each legal move in the current configuration
	// then make that move.  This function will be called recursively from the moveAndPlanOn() to attempt to win from the new configuration
	bool cycleThroughCards() {
		stackSize++;

		bool success = false;
		var allBoardMoves = new List<Move>();

		// abandon the game if we've done too many moves
		if (totalMoves > ABANDON_LIMIT) {
			abandoned = true;
			return true;
		}

		// iterate through all tableau stacks and cells, collating the legal moves into allBoardMoves
		for (int stackIndex = 0; stackIndex < 14; stackIndex++) {
			Position source;
			if (stackIndex > 3) {
				source = new Position { index = stackIndex-4, type = StackType.TABLEAU };
			} else {
				source = new Position { index = stackIndex , type = StackType.CELL };
			}

			var move = board.findLegalMove(source);
			if (move != null) {
				allBoardMoves.Add((Move)move);
			}
		}

		allBoardMoves = allBoardMoves.OrderBy(x => x.target.type).ToList(); // sort by target type, so we move to goals first, then tableaus, then cells
		foreach (var move in allBoardMoves) {
			success = moveAndPlayOne(move);
			if (success) break;
		}

		stackSize--;
		return success;

	}

	public void replayGame() {
		var moveCopy = new List<Move>(gameMoves);
		foreach (var move in moveCopy) {
			undoLastMove();
			Console.WriteLine(board.ToString());
			Thread.Sleep(50);

		}

		foreach (var move in moveCopy) {
			moveCard(move.source, move.target, move.extent);
			if ( move.extent <= 1) {
				Console.WriteLine(board.ToString());
				Thread.Sleep(50);
			}
		}
	}

	public void print() {
		// Console.SetBufferSize(200, 40);
		Console.Clear();

		var offsetY = 2;

		for (int i = 0; i < 4; i++) {
			var card = board.goals[i].OptionalPeek();
			Console.SetCursorPosition(1+(i *4),offsetY+1);
			Card.write(board.goals[i].OptionalPeek()," - ");
		}

		for (int i = 0; i < 4; i++) {
			var card = board.cells[i].OptionalPeek();
			Console.SetCursorPosition(30+(i *4),offsetY+1);
			Card.write(board.cells[i].OptionalPeek()," - ");
		}

		var maxLength = board.stacks.Aggregate(0, (acc, stack) => Math.Max(acc, stack.Count()));
		for (int row = 0; row < maxLength; row++) {
			for (int col = 0; col < 10; col++) {
				Console.SetCursorPosition(1+(col*4),offsetY+3+row);
				var stack = board.stacks[col];
				var card = stack.OptionalElementAt(stack.Count()-row -1);
				
				Card.write(card," - ");
			}
		}

		Console.SetCursorPosition(0,offsetY+15);
		Console.ForegroundColor = ConsoleColor.White;
		Console.WriteLine("Games Played: " + tally.totalGames);
		Console.WriteLine("Winnable: " + tally.winnable+ " Losers: " + tally.losers + " Abandoned: " + tally.abandoned);
		Console.WriteLine("Stack Size: " + stackSize);
		Console.WriteLine("Total Moves: " + totalMoves);
		Console.WriteLine("Unique Boards: " + memory.size() + " Repeats Avoided: " + memory.repeatsAvoided);
		Console.WriteLine("MaxLength: " + maxLength);
		
	}


	// main function
	public static void Main() {

		var tally = new Tally();
		foreach (int _ in Enumerable.Range(0, 1000)) {
			var memory = new BoardMemory();
			var game = new Game(tally, memory);

			game.print();
			game.cycleThroughCards();
			if (game.abandoned) {
				tally.abandoned++;
			} else if (game.board.isSuccess()) {
				tally.winnable++;
			} else {
				tally.losers++;
			}
			tally.totalGames++;
		}


	}



}

