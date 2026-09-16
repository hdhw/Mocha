# 📝 Mocha TODO

## 🔴 High Priority

* [ ] **Fix Cheering**

  * Make cheering behave like real Rec Room.
  * Players can give **15 cheers per day**.
  * Cheers should **not accumulate beyond the daily limit**.
  * The player's received-cheer value should remain at **`1`** once they've been cheered.

    * This indicates that the player has been cheered.
    * Allows the player to use the **CheerBadge**.
    * Receiving additional cheers should **not increment this value**.

* [ ] **Fix Invites, Favorites & Subscribing**

  * Fix invites not working correctly.
  * Fix favoriting functionality.
  * Fix subscribing functionality.

* [ ] **Fix Private Instance Invites**

  * Investigate why invites aren't working in private instances.
  * Verify the complete invite flow between the client, server, and instance permissions.
  * Confirm that invited players can actually join the intended private instance.

* [ ] **Fix Live Token Updates**

  * Tokens should update immediately when earned or spent.
  * Make sure the client receives the updated token balance without requiring a refresh or reload.
  * Also ensure the token amount does not double client sidedly.

* [ ] **Fix Shop Items**

  * Prevent items from appearing in the shop when they shouldn't be available.
  * Verify the conditions determining whether an item is displayed.

* [ ] **Fix GameRewards**

  * Make sure the correct reward flow is triggered.
  * Either:

    * 🎁 **Gift the reward correctly**, or
    * 💬 **Display the correct prompt using the correct DTO**.

* [ ] **Fix Sample Audios**

  * Sample audios are currently being **Kirked**.
  * Add all required sample audios and ensure they're properly available.

## 🟢 Completed

* [x] ~~**Make `Enums.cs`**~~
* [x] ~~**Being able to copy Rec Center**~~ 💀
* [x] **Figure out if Izzy is genuinely smart or not...**

  * Conclusion: She clearly is not very smart. 💀
* [x] Other stuff apparently done not too long ago by **izzy!**

  * lame ass todo 💀
