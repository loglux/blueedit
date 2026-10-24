# Lab report plan

- Write up the method first, while the details of the set-up are still fresh, then the results, and the introduction last of all.
- [ ] Ask the demonstrator whether the uncertainty of each reading can be taken as half of the smallest scale division.

| Part           | Words | Due    |
|:---------------|:-----:|-------:|
| Method         |  400  |  3 Nov |
| **Results**    |  600  | 10 Nov |
| Discussion     |  500  | 17 Nov |

```cs
// readings in millimetres, five per sample
static double Mean(double[] readings) => readings.Sum() / readings.Length;
```

```python
def spread(readings):
    return max(readings) - min(readings)  # range, not the standard deviation
```
